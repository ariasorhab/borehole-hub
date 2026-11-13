using System.Globalization;
using System.Text.RegularExpressions;
using BoreholeHub.Core.Entities;
using BoreholeHub.Core.Services;
using BoreholeHub.Web.Data;
using BoreholeHub.Web.Import;
using BoreholeHub.Web.Localization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Localization;

namespace BoreholeHub.Web.Pages.Projekte;

/// <summary>
/// Tablet-friendly field form: records one new Kleinrammbohrung (with layers) or Rammsondierung (with N10 values).
/// The candidate is checked with <see cref="DataQualityService"/> before it is saved; a Fehler blocks saving.
/// </summary>
public class FeldModel(BoreholeDbContext db, IStringLocalizer<FeldModel> loc, IStringLocalizer<PruefTexte> pruefTexte) : PageModel
{
    public const int StandardSchritte = 40;   // 4,00 m
    public const int MaxSchritte = 100;       // 10,00 m
    public const int MaxBezeichnung = 20;     // length of the database column Aufschluss.Bezeichnung
    public const int LeereZeilen = 3;

    /// <summary>Allowed values of the Bodengruppe select (the empty entry is added by the view).</summary>
    public static readonly string[] Bodengruppen =
        ["GE", "GW", "GI", "GU", "GU*", "GT", "GT*", "SE", "SW", "SI", "SU", "SU*", "ST", "ST*", "UL", "UM", "UA", "TL", "TM", "TA", "OU", "OT", "OH", "OK", "HN", "HZ", "F", "A"];

    public static readonly string[] Frostklassen = ["F1", "F2", "F3"];

    private static readonly CultureInfo De = CultureInfo.GetCultureInfo("de-DE");

    // TempData keys: a successful save redirects to the GET, which shows its result once.
    private const string TempProjekt = "FeldProjekt";
    private const string TempBezeichnung = "FeldBezeichnung";
    private const string TempAufschlussId = "FeldAufschlussId";
    private const string TempHinweise = "FeldHinweise";

    /// <summary>
    /// One error message. <see cref="Key"/> is the id of the field (or section) it belongs to.
    /// <see cref="Text"/> is the German text; <see cref="Vorlage"/> and <see cref="Argumente"/> are the German
    /// template and arguments that the view localizes (rule b); <see cref="Befund"/> is set for data-check findings.
    /// </summary>
    public record FeldFehler(string Key, string Text, string Vorlage = "", IReadOnlyList<string>? Argumente = null, PruefBefund? Befund = null);

    /// <summary>One layer row as typed by the user (all values are kept as text).</summary>
    public class SchichtZeile
    {
        public string Von { get; set; } = "";
        public string Bis { get; set; } = "";
        public string Kurzzeichen { get; set; } = "";
        public string Beschreibung { get; set; } = "";
        public string Bodengruppe { get; set; } = "";
        public string Farbe { get; set; } = "";
        public string Zustand { get; set; } = "";
        public string Homogenbereich { get; set; } = "";
        public string Frostklasse { get; set; } = "";
        public string Kf { get; set; } = "";

        public bool IstLeer =>
            string.IsNullOrWhiteSpace(Von) && string.IsNullOrWhiteSpace(Bis) && string.IsNullOrWhiteSpace(Kurzzeichen)
            && string.IsNullOrWhiteSpace(Beschreibung) && string.IsNullOrWhiteSpace(Bodengruppe) && string.IsNullOrWhiteSpace(Farbe)
            && string.IsNullOrWhiteSpace(Zustand) && string.IsNullOrWhiteSpace(Homogenbereich) && string.IsNullOrWhiteSpace(Frostklasse)
            && string.IsNullOrWhiteSpace(Kf);
    }

    public Project Projekt { get; private set; } = null!;

    // ---------- form values ----------

    [BindProperty] public string? Art { get; set; } = "KRB";
    [BindProperty] public string? Bezeichnung { get; set; }
    [BindProperty] public string? Datum { get; set; }
    [BindProperty] public string? Rechtswert { get; set; }
    [BindProperty] public string? Hochwert { get; set; }
    [BindProperty] public string? Ansatzhoehe { get; set; }
    [BindProperty] public string? Endteufe { get; set; }
    [BindProperty] public string? Grundwasser { get; set; }
    [BindProperty] public string? Bemerkung { get; set; }

    public List<SchichtZeile> Zeilen { get; private set; } = [];

    /// <summary>Typed N10 values by step number (1 = 0,10 m).</summary>
    public Dictionary<int, string> N10 { get; private set; } = [];

    // ---------- view data ----------

    public string PrefillKrb { get; private set; } = "KRB 1";
    public string PrefillDph { get; private set; } = "DPH 1";
    public IReadOnlyList<string> Homogenbereiche { get; private set; } = [];
    public int Schritte { get; private set; } = StandardSchritte;

    public IReadOnlyList<FeldFehler> Fehler { get; private set; } = [];
    public IReadOnlyList<PruefBefund> Befunde { get; private set; } = [];
    public HashSet<int> N10Ungueltig { get; private set; } = [];

    /// <summary>True after a POST that did not save.</summary>
    public bool NichtGespeichert { get; private set; }

    // ---------- success box (after a redirect) ----------

    public bool Gespeichert { get; private set; }
    public string GespeichertName { get; private set; } = "";
    public int GespeichertId { get; private set; }
    public IReadOnlyList<PruefBefund> GespeichertHinweise { get; private set; } = [];

    public bool IstDph => string.Equals(Art, "DPH", StringComparison.Ordinal);

    public IEnumerable<FeldFehler> FehlerFuer(string key) => Fehler.Where(f => f.Key == key);

    public bool HatFehler(string key) => Fehler.Any(f => f.Key == key);

    // ---------- texts for the view and the script (rule b and rule e) ----------

    /// <summary>The text of one error, in the current language.</summary>
    public string Fehlertext(FeldFehler f)
    {
        if (f.Befund is not null) { return BefundText(f.Befund); }
        return f.Vorlage.Length == 0 ? f.Text : loc[f.Vorlage, (f.Argumente ?? []).ToArray<object>()].Value;
    }

    /// <summary>A data-check finding as "Rule: message", in the current language.</summary>
    public string BefundText(PruefBefund b) => PruefTexte.Regel(pruefTexte, b.Regel) + ": " + PruefTexte.Meldung(pruefTexte, b);

    public string MsgStandortNichtVerfuegbar => loc["Standort nicht verfügbar – bitte Koordinaten eingeben."].Value;
    public string MsgStandortErmittelt => loc["Standort wird ermittelt …"].Value;
    public string MsgGenauigkeit => loc["Genauigkeit ± {0} m", "{0}"].Value;
    public string MsgN10Bis => loc["N10 bis {0} m", "{0}"].Value;

    /// <summary>Accessible name of one N10 input, for example "N10 up to 0,10 m".</summary>
    public string N10Beschriftung(int schritt) => loc["N10 bis {0} m", (schritt / 10.0).ToString("0.00", De)].Value;

    // ========== GET ==========

    public async Task<IActionResult> OnGetAsync(int id)
    {
        if (!await LadeProjektAsync(id)) { return NotFound(); }

        await LadeViewDatenAsync(id);
        Art = "KRB";
        Bezeichnung = PrefillKrb;
        Datum = DateOnly.FromDateTime(DateTime.Today).ToString("yyyy-MM-dd", CultureInfo.InvariantCulture);
        Zeilen = LeereZeilenListe(LeereZeilen);
        Schritte = StandardSchritte;
        LeseErgebnis(id);
        return Page();
    }

    /// <summary>GET ?handler=Utm&amp;lat=..&amp;lon=..: converts WGS84 to ETRS89 / UTM 32N (EPSG:25832), rounded to 0,1 m.</summary>
    public async Task<IActionResult> OnGetUtmAsync(int id, string? lat, string? lon)
    {
        if (!await db.Projects.AsNoTracking().AnyAsync(p => p.Id == id)) { return NotFound(); }

        if (!TryInvariant(lat, out double la) || !TryInvariant(lon, out double lo)
            || la < 46 || la > 56 || lo < 5 || lo > 16)
        {
            return new JsonResult(new { fehler = "lat und lon müssen Zahlen mit Punkt im Bereich Deutschlands sein." }) { StatusCode = 400 };
        }

        var (rechts, hoch) = CoordinateService.ToUtm32(la, lo);
        return new JsonResult(new { rechtswert = Math.Round(rechts, 1), hochwert = Math.Round(hoch, 1) });
    }

    private static bool TryInvariant(string? text, out double value)
    {
        value = 0;
        return !string.IsNullOrWhiteSpace(text)
            && double.TryParse(text.Trim(), NumberStyles.Float, CultureInfo.InvariantCulture, out value)
            && double.IsFinite(value);
    }

    private void LeseErgebnis(int id)
    {
        object? projekt = TempData[TempProjekt];
        object? name = TempData[TempBezeichnung];
        object? aufschlussId = TempData[TempAufschlussId];
        object? hinweise = TempData[TempHinweise];
        if (projekt is int pid && pid == id && name is string n && aufschlussId is int aid)
        {
            Gespeichert = true;
            GespeichertName = n;
            GespeichertId = aid;
            GespeichertHinweise = hinweise is string h && h.Length > 0
                ? h.Split('\n').Select(DekodiereHinweis).ToList()
                : new List<PruefBefund>();
        }
    }

    // ========== POST ==========

    public async Task<IActionResult> OnPostAsync(int id)
    {
        if (!await LadeProjektAsync(id)) { return NotFound(); }

        var existing = await PruefungModel.LadeAufschluesseAsync(db, id);
        await LadeViewDatenAsync(id, existing);

        Art = Art?.Trim() ?? "";
        Bezeichnung = Bezeichnung?.Trim() ?? "";
        Datum = Datum?.Trim() ?? "";
        Rechtswert = Rechtswert?.Trim() ?? "";
        Hochwert = Hochwert?.Trim() ?? "";
        Ansatzhoehe = Ansatzhoehe?.Trim() ?? "";
        Endteufe = Endteufe?.Trim() ?? "";
        Grundwasser = Grundwasser?.Trim() ?? "";
        Bemerkung = Bemerkung?.Trim() ?? "";
        LeseFormular();

        var fehler = new List<FeldFehler>();
        if (existing.Count >= DatenModel.MaxAufschluesseProProjekt)
        {
            // Hosting limit (D166): no more Aufschlüsse once the project holds the maximum.
            fehler.Add(Fehl("bezeichnung", "Das Projekt enthält bereits 200 Aufschlüsse; weitere können nicht gespeichert werden."));
        }

        var kandidat = Validiere(existing, fehler);
        NichtGespeichert = true;

        if (kandidat is null || fehler.Count > 0)
        {
            Fehler = fehler;
            return Page();
        }

        // Quality check: all existing Aufschlüsse plus the candidate; only the findings of the candidate count.
        kandidat.ProjectId = id;
        var alle = new List<Aufschluss>(existing) { kandidat };
        var befunde = DataQualityService.Pruefen(alle)
            .Where(b => string.Equals(b.Aufschluss, kandidat.Bezeichnung, StringComparison.Ordinal))
            .ToList();
        Befunde = befunde;

        var fehlerBefunde = befunde.Where(b => b.Stufe == PruefStufe.Fehler).ToList();
        if (fehlerBefunde.Count > 0)
        {
            Fehler = fehlerBefunde
                .Select(b => new FeldFehler(FeldFuerRegel(b.Regel), $"{b.Regel}: {b.Meldung}", "", null, b))
                .ToList();
            return Page();
        }

        // One SaveChanges = one transaction: the Aufschluss with its Schichten / Messwerte.
        db.Aufschluesse.Add(kandidat);
        await db.SaveChangesAsync();

        var hinweise = befunde.Where(b => b.Stufe == PruefStufe.Hinweis).Select(KodiereHinweis).ToList();
        TempData[TempProjekt] = id;
        TempData[TempBezeichnung] = kandidat.Bezeichnung;
        TempData[TempAufschlussId] = kandidat.Id;
        TempData[TempHinweise] = string.Join('\n', hinweise);
        return RedirectToPage(new { id });
    }

    // A hinweis is kept between POST and GET as German rule, German text, German template and arguments (rule b).
    private const char Trenner = '\u001F';

    private static string KodiereHinweis(PruefBefund b) =>
        string.Join(Trenner, new[] { b.Regel, b.Meldung, b.Vorlage }.Concat(b.Argumente));

    private static PruefBefund DekodiereHinweis(string zeile)
    {
        var teile = zeile.Split(Trenner);
        return new PruefBefund(teile[0], PruefStufe.Hinweis, 0, "", teile[1]) { Vorlage = teile[2], Argumente = teile.Skip(3).ToList() };
    }

    private static string FeldFuerRegel(string regel) => regel switch
    {
        "Schichtfolge" or "Versickerung" or "Proben" => "schichten",
        "Endteufe" => "endteufe",
        "Koordinaten" => "rechtswert",
        "Ansatzhöhe" => "ansatzhoehe",
        "Rammsondierung" => "n10",
        _ => "bezeichnung",
    };

    /// <summary>A German error: the template is formatted with de-DE, the arguments are already German text.</summary>
    private static FeldFehler Fehl(string key, string vorlage, params string[] argumente) =>
        new(key, string.Format(De, vorlage, argumente.ToArray<object>()), vorlage, argumente);

    // ---------- reading the raw form ----------

    private static readonly Regex SchichtName = new(@"^Schichten\[(\d+)\]\.(\w+)$", RegexOptions.Compiled);
    private static readonly Regex N10Name = new(@"^N10\[(\d+)\]$", RegexOptions.Compiled);

    /// <summary>Reads the layer rows and the N10 cells. The row numbers may have gaps (JS removed a row).</summary>
    private void LeseFormular()
    {
        var zeilen = new SortedDictionary<int, SchichtZeile>();
        var n10 = new Dictionary<int, string>();
        foreach (var key in Request.Form.Keys)
        {
            string wert = Request.Form[key].ToString().Trim();
            var m = SchichtName.Match(key);
            if (m.Success && int.TryParse(m.Groups[1].Value, out int index) && index < 1000)
            {
                if (!zeilen.TryGetValue(index, out var z)) { zeilen[index] = z = new SchichtZeile(); }
                switch (m.Groups[2].Value)
                {
                    case "Von": z.Von = wert; break;
                    case "Bis": z.Bis = wert; break;
                    case "Kurzzeichen": z.Kurzzeichen = wert; break;
                    case "Beschreibung": z.Beschreibung = wert; break;
                    case "Bodengruppe": z.Bodengruppe = wert; break;
                    case "Farbe": z.Farbe = wert; break;
                    case "Zustand": z.Zustand = wert; break;
                    case "Homogenbereich": z.Homogenbereich = wert; break;
                    case "Frostklasse": z.Frostklasse = wert; break;
                    case "Kf": z.Kf = wert; break;
                }

                continue;
            }

            m = N10Name.Match(key);
            if (m.Success && int.TryParse(m.Groups[1].Value, out int schritt) && schritt >= 1 && schritt <= MaxSchritte && wert.Length > 0)
            {
                n10[schritt] = wert;
            }
        }

        Zeilen = zeilen.Values.ToList();
        N10 = n10;
    }

    // ---------- validation ----------

    private Aufschluss? Validiere(IReadOnlyList<Aufschluss> existing, List<FeldFehler> fehler)
    {
        bool krb = Art == "KRB";
        bool dph = Art == "DPH";
        if (!krb && !dph) { fehler.Add(Fehl("art", "Art: Bitte Kleinrammbohrung oder Rammsondierung wählen.")); }

        string name = Bezeichnung ?? "";
        if (name.Length == 0) { fehler.Add(Fehl("bezeichnung", "Bezeichnung: Bitte eine Bezeichnung eingeben.")); }
        else if (name.Length > MaxBezeichnung) { fehler.Add(Fehl("bezeichnung", "Bezeichnung: Höchstens {0} Zeichen.", MaxBezeichnung.ToString(De))); }
        else if (existing.Any(a => string.Equals(a.Bezeichnung?.Trim(), name, StringComparison.OrdinalIgnoreCase)))
        {
            fehler.Add(Fehl("bezeichnung", "Bezeichnung „{0}“ ist in diesem Projekt bereits vergeben.", name));
        }

        var datum = default(DateOnly);
        if (!DateOnly.TryParseExact(Datum, "yyyy-MM-dd", CultureInfo.InvariantCulture, DateTimeStyles.None, out datum))
        {
            fehler.Add(Fehl("datum", "Datum: Bitte ein Datum eingeben."));
        }
        else if (datum < CsvImportService.MinDatum || datum > CsvImportService.MaxDatum())
        {
            fehler.Add(Fehl("datum", "Datum: Bitte ein Datum zwischen 01.01.2000 und {0} eingeben.",
                CsvImportService.MaxDatum().ToString("dd.MM.yyyy", CultureInfo.InvariantCulture)));
        }

        double rechts = Pflichtzahl(Rechtswert, "rechtswert", "Rechtswert: Bitte eine Zahl eingeben.", fehler);
        double hoch = Pflichtzahl(Hochwert, "hochwert", "Hochwert: Bitte eine Zahl eingeben.", fehler);
        double hoehe = Pflichtzahl(Ansatzhoehe, "ansatzhoehe", "Ansatzhöhe: Bitte eine Zahl eingeben.", fehler);
        double endteufe = Pflichtzahl(Endteufe, "endteufe", "Endteufe: Bitte eine Zahl eingeben.", fehler);

        double? grundwasser = null;
        if (!string.IsNullOrEmpty(Grundwasser))
        {
            // TryNumber also refuses infinity and NaN, which get the number message.
            if (!CsvImportService.TryNumber(Grundwasser, out double gw))
            {
                fehler.Add(Fehl("grundwasser", "Grundwasser: Bitte eine Zahl eingeben."));
            }
            else if (gw < 0)
            {
                fehler.Add(Fehl("grundwasser", "Grundwasser: Der Wert darf nicht negativ sein."));
            }
            else if (endteufe > 0 && gw > endteufe + 0.005)
            {
                fehler.Add(Fehl("grundwasser", "Grundwasser: Der Wert liegt tiefer als die Endteufe."));
            }
            else { grundwasser = gw; }
        }

        var schichten = new List<Schicht>();
        var messwerte = new List<RammMesswert>();
        if (krb) { PruefeSchichten(schichten, fehler); }
        if (dph) { PruefeN10(messwerte, fehler); }

        if (fehler.Count > 0) { return null; }

        var a = new Aufschluss
        {
            Bezeichnung = name,
            Art = krb ? AufschlussArt.Kleinrammbohrung : AufschlussArt.Rammsondierung,
            Rechtswert = rechts,
            Hochwert = hoch,
            AnsatzhoeheMNhn = hoehe,
            EndteufeM = endteufe,
            Datum = datum,
            GrundwasserM = grundwasser,
            Bemerkung = string.IsNullOrEmpty(Bemerkung) ? null : Bemerkung,
        };
        a.Schichten.AddRange(schichten);
        a.Messwerte.AddRange(messwerte);
        return a;
    }

    private static double Pflichtzahl(string? text, string key, string vorlage, List<FeldFehler> fehler)
    {
        if (!string.IsNullOrEmpty(text) && CsvImportService.TryNumber(text, out double v)) { return v; }
        fehler.Add(Fehl(key, vorlage));
        return 0;
    }

    private void PruefeSchichten(List<Schicht> schichten, List<FeldFehler> fehler)
    {
        int n = -1; // position among the non-empty rows, the same order AnzuzeigendeZeilen() shows
        for (int i = 0; i < Zeilen.Count; i++)
        {
            var z = Zeilen[i];
            if (z.IstLeer) { continue; }

            n++;
            string key = $"schicht-{n}";
            string nr = (n + 1).ToString(De);
            int vorher = fehler.Count;

            bool vonOk = CsvImportService.TryNumber(z.Von, out double von);
            bool bisOk = CsvImportService.TryNumber(z.Bis, out double bis);
            if (!vonOk) { fehler.Add(Fehl(key, "Schicht {0}, Von: Bitte eine Zahl eingeben.", nr)); }
            if (!bisOk) { fehler.Add(Fehl(key, "Schicht {0}, Bis: Bitte eine Zahl eingeben.", nr)); }
            if (z.Bodengruppe.Length == 0 && z.Kurzzeichen.Length == 0)
            {
                fehler.Add(Fehl(key, "Schicht {0}: Bodengruppe oder Kurzzeichen angeben.", nr));
            }

            double? kf = null;
            if (z.Kf.Length > 0)
            {
                if (!CsvImportService.TryNumber(z.Kf, out double k)) { fehler.Add(Fehl(key, "Schicht {0}, kf: Bitte eine Zahl eingeben (z. B. 2e-5 oder 0,00002).", nr)); }
                else if (k <= 0) { fehler.Add(Fehl(key, "Schicht {0}, kf: Der Wert muss größer als 0 sein.", nr)); }
                else { kf = k; }
            }

            Laenge(z.Kurzzeichen, 40, "Schicht {0}, Kurzzeichen: Höchstens {1} Zeichen.", key, nr, fehler);
            Laenge(z.Beschreibung, 400, "Schicht {0}, Beschreibung: Höchstens {1} Zeichen.", key, nr, fehler);
            Laenge(z.Bodengruppe, 10, "Schicht {0}, Bodengruppe: Höchstens {1} Zeichen.", key, nr, fehler);
            Laenge(z.Farbe, 60, "Schicht {0}, Farbe: Höchstens {1} Zeichen.", key, nr, fehler);
            Laenge(z.Zustand, 60, "Schicht {0}, Zustand: Höchstens {1} Zeichen.", key, nr, fehler);
            Laenge(z.Homogenbereich, 10, "Schicht {0}, Homogenbereich: Höchstens {1} Zeichen.", key, nr, fehler);

            string? frost = null;
            if (z.Frostklasse.Length > 0)
            {
                string f = z.Frostklasse.ToUpperInvariant();
                if (Frostklassen.Contains(f)) { frost = f; }
                else { fehler.Add(Fehl(key, "Schicht {0}, Frostklasse: Erlaubt sind F1, F2, F3 oder leer.", nr)); }
            }

            if (fehler.Count != vorher) { continue; }

            schichten.Add(new Schicht
            {
                VonM = von,
                BisM = bis,
                Kurzzeichen = z.Kurzzeichen,
                Beschreibung = z.Beschreibung,
                Bodengruppe = z.Bodengruppe.Length == 0 ? null : z.Bodengruppe,
                Farbe = z.Farbe.Length == 0 ? null : z.Farbe,
                Zustand = z.Zustand.Length == 0 ? null : z.Zustand,
                Homogenbereich = z.Homogenbereich.Length == 0 ? null : z.Homogenbereich,
                Frostklasse = frost,
                KfWertMs = kf,
            });
        }
    }

    private static void Laenge(string wert, int max, string vorlage, string key, string nr, List<FeldFehler> fehler)
    {
        if (wert.Length > max) { fehler.Add(Fehl(key, vorlage, nr, max.ToString(De))); }
    }

    private void PruefeN10(List<RammMesswert> messwerte, List<FeldFehler> fehler)
    {
        var ungueltig = new HashSet<int>();
        foreach (var (schritt, text) in N10.OrderBy(p => p.Key))
        {
            double tiefe = Math.Round(schritt * 0.1, 1);
            if (int.TryParse(text, NumberStyles.None, CultureInfo.InvariantCulture, out int n))
            {
                messwerte.Add(new RammMesswert { TiefeBisM = tiefe, N10 = n });
            }
            else
            {
                ungueltig.Add(schritt);
                fehler.Add(Fehl("n10", "N10 bei {0} m: Bitte eine ganze Zahl ab 0 eingeben.", tiefe.ToString("0.00", De)));
            }
        }

        N10Ungueltig = ungueltig;
    }

    // ---------- loading ----------

    private async Task<bool> LadeProjektAsync(int id)
    {
        var projekt = await db.Projects.AsNoTracking().FirstOrDefaultAsync(p => p.Id == id);
        if (projekt is null) { return false; }
        Projekt = projekt;
        return true;
    }

    private async Task LadeViewDatenAsync(int id, IReadOnlyList<Aufschluss>? existing = null)
    {
        existing ??= await PruefungModel.LadeAufschluesseAsync(db, id);
        PrefillKrb = NaechsterName(existing, "KRB");
        PrefillDph = NaechsterName(existing, "DPH");
        Homogenbereiche = existing.SelectMany(a => a.Schichten)
            .Select(s => s.Homogenbereich?.Trim())
            .Where(h => !string.IsNullOrEmpty(h))
            .Select(h => h!)
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .OrderBy(h => h, StringComparer.Ordinal)
            .ToList();
    }

    /// <summary>"KRB {max+1}" from the existing names of the project that have this prefix.</summary>
    public static string NaechsterName(IEnumerable<Aufschluss> existing, string prefix)
    {
        var regex = new Regex($@"^{prefix}\s*(\d{{1,6}})$", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);
        int max = 0;
        foreach (var a in existing)
        {
            var m = regex.Match((a.Bezeichnung ?? "").Trim());
            if (m.Success) { max = Math.Max(max, int.Parse(m.Groups[1].Value, CultureInfo.InvariantCulture)); }
        }

        return $"{prefix} {max + 1}";
    }

    private static List<SchichtZeile> LeereZeilenListe(int n) => Enumerable.Range(0, n).Select(_ => new SchichtZeile()).ToList();

    /// <summary>Called by the view: the rows to show (typed rows, padded with empty rows up to three).</summary>
    public IReadOnlyList<SchichtZeile> AnzuzeigendeZeilen()
    {
        var z = Zeilen.Where(r => !r.IstLeer).ToList();
        while (z.Count < LeereZeilen) { z.Add(new SchichtZeile()); }
        return z;
    }

    /// <summary>Number of N10 inputs to render: from the Endteufe (rounded up to 0,10 m), 40 if empty or invalid, at most 100.</summary>
    public int AnzahlSchritte()
    {
        int n = StandardSchritte;
        if (CsvImportService.TryNumber(Endteufe ?? "", out double e) && e > 0)
        {
            n = (int)Math.Ceiling(e * 10 - 1e-9);
        }

        if (N10.Count > 0) { n = Math.Max(n, N10.Keys.Max()); }
        return Math.Clamp(n, 1, MaxSchritte);
    }

    public static string Plural(int n, string einzahl, string mehrzahl) => n == 1 ? $"{n} {einzahl}" : $"{n} {mehrzahl}";

    public string PruefungText => GespeichertHinweise.Count == 0
        ? loc["Prüfung: keine Befunde."].Value
        : GespeichertHinweise.Count == 1
            ? loc["Prüfung: 1 Hinweis."].Value
            : loc["Prüfung: {0} Hinweise.", GespeichertHinweise.Count.ToString(De)].Value;
}
