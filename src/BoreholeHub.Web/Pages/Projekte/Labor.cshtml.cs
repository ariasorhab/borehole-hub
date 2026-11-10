using System.Globalization;
using BoreholeHub.Core.Entities;
using BoreholeHub.Core.Services;
using BoreholeHub.Web.Data;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Localization;

namespace BoreholeHub.Web.Pages.Projekte;

public class LaborModel(BoreholeDbContext db, IStringLocalizer<LaborModel> loc) : PageModel
{
    public const string Beispielhinweis = "Beispielwerte zur Demonstration – keine amtlichen Werte der Ersatzbaustoffverordnung (EBV).";

    private static readonly CultureInfo De = CultureInfo.GetCultureInfo("de-DE");

    /// <summary>One row of a sample card.</summary>
    public record WertZeile(string Parameter, string Wert, string Einheit, string Einstufung, string BadgeCss, int Rang);

    /// <summary>One sample card.</summary>
    public record ProbeKarte(int ProbeId, string Bezeichnung, string Tiefe, string Schicht, string Gesamt, string GesamtCss, int Rang, IReadOnlyList<WertZeile> Zeilen)
    {
        /// <summary>Worse than the strictest class (a higher class or an exceedance).</summary>
        public bool Auffaellig => Rang > 1;

        public bool Ueberschreitung => Rang == int.MaxValue;
    }

    public record VersickerungZeile(string Aufschluss, string Schicht, string Kf, string Css, string Ergebnis, string Grund);

    public Project Projekt { get; private set; } = null!;

    public IReadOnlyList<ProbeKarte> Proben { get; private set; } = [];

    public IReadOnlyList<VersickerungZeile> Versickerung { get; private set; } = [];

    /// <summary>Class names in order BM-0 ... BM-F3 with their example limits per parameter.</summary>
    public IReadOnlyList<string> Klassen { get; private set; } = [];

    public IReadOnlyList<(string Parameter, IReadOnlyList<string> Werte)> Grenzen { get; private set; } = [];

    public int AnzahlProben => Proben.Count;

    public int AnzahlBm0 => Proben.Count(p => p.Rang == 1);

    public int AnzahlHoehere => Proben.Count(p => p.Rang > 1 && p.Rang != int.MaxValue);

    public int AnzahlUeberschreitung => Proben.Count(p => p.Ueberschreitung);

    public async Task<IActionResult> OnGetAsync(int id)
    {
        var projekt = await db.Projects.AsNoTracking().FirstOrDefaultAsync(p => p.Id == id);
        if (projekt is null)
        {
            return NotFound();
        }

        Projekt = projekt;
        var aufschluesse = await db.Aufschluesse.AsNoTracking()
            .Where(a => a.ProjectId == id)
            .Include(a => a.Schichten)
            .Include(a => a.Proben).ThenInclude(p => p.Laborwerte)
            .AsSplitQuery()
            .OrderBy(a => a.Id)
            .ToListAsync();
        var grenzwerte = await db.Grenzwerte.AsNoTracking().OrderBy(g => g.Reihenfolge).ToListAsync();

        Klassen = grenzwerte.GroupBy(g => g.Klasse).OrderBy(g => g.Min(x => x.Reihenfolge)).Select(g => g.Key).ToList();
        Grenzen = grenzwerte.GroupBy(g => g.Parameter)
            .Select(g => (g.Key, (IReadOnlyList<string>)g.OrderBy(x => x.Reihenfolge).Select(x => x.Wert.ToString("0.##", De)).ToList()))
            .ToList();

        var karten = new List<ProbeKarte>();
        foreach (var a in aufschluesse)
        {
            foreach (var p in a.Proben.OrderBy(p => p.VonM))
            {
                karten.Add(BuildKarte(a, p, grenzwerte));
            }
        }

        Proben = karten;
        Versickerung = aufschluesse
            .SelectMany(a => a.Schichten.Where(s => s.KfWertMs is not null).OrderBy(s => s.VonM).Select(s => BuildVersickerung(a, s)))
            .ToList();
        return Page();
    }

    private static ProbeKarte BuildKarte(Aufschluss a, Probe p, List<Grenzwert> grenzwerte)
    {
        double mitte = (p.VonM + p.BisM) / 2;
        var schicht = a.Schichten.FirstOrDefault(s => s.VonM <= mitte && mitte < s.BisM);
        string schichtName = schicht is null ? "–" : LayerName(schicht);
        string tiefe = $"{p.VonM.ToString("0.00", De)}–{p.BisM.ToString("0.00", De)} m";

        var ergebnisse = LabCheckService.Evaluate(p.Laborwerte.OrderBy(l => l.Id), grenzwerte);
        var zeilen = new List<WertZeile>();
        foreach (var r in ergebnisse)
        {
            int rang;
            string text, css;
            if (r.Ueberschritten)
            {
                rang = int.MaxValue;
                text = "> " + r.UeberschritteneGrenze;
                css = "badge-bad";
            }
            else if (r.EingehalteneKlasse is not null)
            {
                rang = grenzwerte.First(g => g.Klasse == r.EingehalteneKlasse && string.Equals(g.Parameter, r.Parameter, StringComparison.OrdinalIgnoreCase)).Reihenfolge;
                text = r.EingehalteneKlasse;
                css = rang == 1 ? "badge-ok" : "badge-warn";
            }
            else
            {
                rang = 0;
                text = "–";
                css = "badge-muted";
            }

            zeilen.Add(new WertZeile(r.Parameter, r.Wert.ToString("0.##", De), r.Einheit, text, css, rang));
        }

        int worst = zeilen.Count == 0 ? 0 : zeilen.Max(z => z.Rang);
        var worstRow = zeilen.FirstOrDefault(z => z.Rang == worst);
        string gesamt = worstRow?.Einstufung ?? "–";
        string gesamtCss = worst == int.MaxValue ? "badge-bad" : worst > 1 ? "badge-warn" : worst == 1 ? "badge-ok" : "badge-muted";
        return new ProbeKarte(p.Id, p.Bezeichnung, tiefe, schichtName, gesamt, gesamtCss, worst, zeilen);
    }

    /// <summary>Layer name for the card title: the word before the colon of the description ("Auffüllung"), else the Kurzzeichen.</summary>
    private static string LayerName(Schicht s)
    {
        int colon = s.Beschreibung.IndexOf(':');
        return colon > 0 ? s.Beschreibung[..colon].Trim() : s.Kurzzeichen;
    }

    private static string Limit(double v) => Aufschluesse.DetailsModel.KfText(v).Replace("1,0 · ", "1 · ");

    private VersickerungZeile BuildVersickerung(Aufschluss a, Schicht s)
    {
        var (css, _) = Aufschluesse.DetailsModel.Versickerung(s.KfWertMs);
        string grund = InfiltrationService.Assess(s.KfWertMs) switch
        {
            InfiltrationResult.Geeignet => loc["kf zwischen {0} und {1} m/s", Limit(InfiltrationService.KfMin), Limit(InfiltrationService.KfMax)].Value,
            InfiltrationResult.ZuGeringDurchlaessig => loc["kf < {0} m/s", Limit(InfiltrationService.KfMin)].Value,
            InfiltrationResult.ZuDurchlaessig => loc["kf > {0} m/s", Limit(InfiltrationService.KfMax)].Value,
            _ => loc["kein kf-Wert"].Value,
        };
        string schicht = $"{s.VonM.ToString("0.00", De)}–{s.BisM.ToString("0.00", De)} m · {s.Kurzzeichen}";
        return new VersickerungZeile(a.Bezeichnung, schicht, Aufschluesse.DetailsModel.KfText(s.KfWertMs), css, ErgebnisText(s.KfWertMs), grund);
    }

    /// <summary>Infiltration result in the UI language. The CSS class comes from the Details helper.</summary>
    private string ErgebnisText(double? kf) => InfiltrationService.Assess(kf) switch
    {
        InfiltrationResult.Geeignet => loc["Geeignet"].Value,
        InfiltrationResult.ZuGeringDurchlaessig => loc["Zu gering durchlässig"].Value,
        InfiltrationResult.ZuDurchlaessig => loc["Zu durchlässig"].Value,
        _ => loc["Unbekannt"].Value,
    };
}
