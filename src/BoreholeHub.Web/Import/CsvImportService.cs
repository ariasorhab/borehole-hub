using System.Globalization;
using System.Text;
using BoreholeHub.Core.Entities;

namespace BoreholeHub.Web.Import;

/// <summary>
/// Result of parsing the two CSV files. When <see cref="Fehler"/> is not empty nothing may be saved
/// and the two lists are empty (import is all-or-nothing).
/// </summary>
/// <param name="NeueAufschluesse">New Aufschlüsse from the Aufschlüsse file (ProjectId is not set).</param>
/// <param name="NeueSchichten">All new layers. Layers of a new Aufschluss point to it via <see cref="Schicht.Aufschluss"/>;
/// layers of an Aufschluss that already exists in the project carry its AufschlussId.</param>
/// <param name="Fehler">German error messages with file name and line number.</param>
public record ImportResult(IReadOnlyList<Aufschluss> NeueAufschluesse, IReadOnlyList<Schicht> NeueSchichten, IReadOnlyList<string> Fehler)
{
    /// <summary>The same errors as <see cref="Fehler"/>, in the same order, as German template and arguments (for the UI language).</summary>
    public IReadOnlyList<ImportFehler> FehlerTexte { get; init; } = Array.Empty<ImportFehler>();
}

/// <summary>One import error: German composite-format template and its arguments, already formatted in de-DE.</summary>
public record ImportFehler(string Vorlage, IReadOnlyList<string> Argumente);

/// <summary>
/// Reads the two CSV formats of the data page (semicolon separated, decimal comma, dd.MM.yyyy) and validates them completely
/// before anything is stored.
/// </summary>
public class CsvImportService
{
    public const string AufschluesseHeader = "Bezeichnung;Art;Rechtswert;Hochwert;Ansatzhoehe_mNHN;Endteufe_m;Datum;Grundwasser_m";
    public const string SchichtenHeader = "Bezeichnung;Von_m;Bis_m;Kurzzeichen;Beschreibung;Bodengruppe;Farbe;Zustand;Homogenbereich;Frostklasse;kf_m_s";

    private const int MaxFehler = 100;
    private const double Tol = 0.0005;
    private static readonly CultureInfo De = CultureInfo.GetCultureInfo("de-DE");

    /// <summary>Earliest accepted Aufschluss date (01.01.2000).</summary>
    public static readonly DateOnly MinDatum = new(2000, 1, 1);

    /// <summary>Latest accepted Aufschluss date: today plus one year (checked on each call).</summary>
    public static DateOnly MaxDatum() => DateOnly.FromDateTime(DateTime.Today).AddYears(1);

    /// <summary>Reports an error of a CSV row. The template uses {0} for the row number and {1} onwards for the arguments.</summary>
    private delegate void ZeilenFehler(string vorlage, int zeile, params string[] argumente);

    /// <summary>Decodes an uploaded file: UTF-8 (with or without BOM); if the bytes are not valid UTF-8, ISO-8859-1 (old Excel "CSV").</summary>
    public static string Decode(byte[] bytes)
    {
        ArgumentNullException.ThrowIfNull(bytes);
        string text;
        try
        {
            text = new UTF8Encoding(false, true).GetString(bytes);
        }
        catch (DecoderFallbackException)
        {
            text = Encoding.Latin1.GetString(bytes);
        }

        return text.TrimStart('﻿');
    }

    /// <summary>Wraps a value in plain double quotes (kept out of the templates so that every key is a literal).</summary>
    private static string Q(string value) => "\"" + value + "\"";

    /// <summary>German text of an error (the same as the entry of <see cref="ImportResult.Fehler"/>).</summary>
    internal static string Deutsch(ImportFehler fehler) => string.Format(De, fehler.Vorlage, fehler.Argumente.ToArray<object>());

    public ImportResult Parse(string? aufschluesseCsv, string? schichtenCsv, IReadOnlyCollection<Aufschluss> existing)
    {
        ArgumentNullException.ThrowIfNull(existing);
        var fehler = new List<ImportFehler>();
        void Err(string vorlage, int zeile, params string[] argumente)
        {
            if (fehler.Count < MaxFehler) { fehler.Add(new ImportFehler(vorlage, [zeile.ToString(De), .. argumente])); }
        }

        bool hasA = !string.IsNullOrWhiteSpace(aufschluesseCsv);
        bool hasS = !string.IsNullOrWhiteSpace(schichtenCsv);
        if (!hasA && !hasS)
        {
            const string keineDatei = "Es wurde keine Datei ausgewählt oder die Datei ist leer.";
            return new ImportResult([], [], [keineDatei]) { FehlerTexte = [new ImportFehler(keineDatei, [])] };
        }

        // ---------- Aufschlüsse ----------
        var neue = new List<Aufschluss>();
        var lineOfNew = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);
        if (hasA)
        {
            var rows = ReadRows(aufschluesseCsv!, AufschluesseHeader, true, 8, Err);
            foreach (var (line, f) in rows)
            {
                var a = ParseAufschluss(line, f, existing, lineOfNew, Err);
                if (a is not null)
                {
                    neue.Add(a);
                    lineOfNew[a.Bezeichnung] = line;
                }
            }
        }

        // ---------- Schichten ----------
        var schichten = new List<Schicht>();
        if (hasS)
        {
            var rows = ReadRows(schichtenCsv!, SchichtenHeader, false, 11, Err);
            var perAufschluss = new Dictionary<string, List<(int Line, Schicht S)>>(StringComparer.OrdinalIgnoreCase);
            foreach (var (line, f) in rows)
            {
                string name = f[0].Trim();
                if (name.Length == 0)
                {
                    Err("Schichten, Zeile {0}: Bezeichnung fehlt.", line);
                    continue;
                }

                var neu = neue.FirstOrDefault(a => string.Equals(a.Bezeichnung, name, StringComparison.OrdinalIgnoreCase));
                var vorhanden = existing.FirstOrDefault(a => string.Equals(a.Bezeichnung, name, StringComparison.OrdinalIgnoreCase));
                var ziel = neu ?? vorhanden;
                if (ziel is null)
                {
                    Err("Schichten, Zeile {0}: Aufschluss {1} ist weder in der Aufschlüsse-Datei noch im Projekt vorhanden.", line, Q(name));
                    continue;
                }

                if (neu is null && vorhanden!.Schichten.Count > 0)
                {
                    Err("Schichten, Zeile {0}: Aufschluss {1} hat im Projekt bereits Schichten; sie können nicht nachträglich ergänzt werden.", line, Q(name));
                    continue;
                }

                if (ziel.Art == AufschlussArt.Rammsondierung)
                {
                    Err("Schichten, Zeile {0}: Rammsondierung {1} kann keine Schichten haben.", line, Q(ziel.Bezeichnung));
                    continue;
                }

                var s = ParseSchicht(line, f, Err);
                if (s is null) { continue; }
                if (neu is not null) { s.Aufschluss = neu; } else { s.AufschlussId = vorhanden!.Id; }
                if (!perAufschluss.TryGetValue(ziel.Bezeichnung, out var list))
                {
                    list = [];
                    perAufschluss[ziel.Bezeichnung] = list;
                }

                list.Add((line, s));
            }

            // continuity from 0 to the Endteufe
            foreach (var (name, list) in perAufschluss)
            {
                var ziel = neue.FirstOrDefault(a => string.Equals(a.Bezeichnung, name, StringComparison.OrdinalIgnoreCase))
                           ?? existing.First(a => string.Equals(a.Bezeichnung, name, StringComparison.OrdinalIgnoreCase));
                var sorted = list.OrderBy(x => x.S.VonM).ThenBy(x => x.Line).ToList();
                double cursor = 0;
                bool first = true;
                foreach (var (line, s) in sorted)
                {
                    if (first && Math.Abs(s.VonM) > Tol)
                    {
                        Err("Schichten, Zeile {0}: Die erste Schicht von {1} beginnt bei {2}, nicht bei 0,00 m.", line, name, M(s.VonM));
                    }
                    else if (!first && s.VonM > cursor + Tol)
                    {
                        Err("Schichten, Zeile {0}: Lücke zwischen {1} und {2} bei {3}", line, M(cursor), M(s.VonM), name);
                    }
                    else if (!first && s.VonM < cursor - Tol)
                    {
                        Err("Schichten, Zeile {0}: Überlappung zwischen {1} und {2} bei {3}", line, M(s.VonM), M(cursor), name);
                    }

                    first = false;
                    cursor = s.BisM;
                }

                if (Math.Abs(cursor - ziel.EndteufeM) > Tol)
                {
                    Err("Schichten, Zeile {0}: Die letzte Schicht von {1} endet bei {2}, die Endteufe ist {3}.", sorted[^1].Line, name, M(cursor), M(ziel.EndteufeM));
                }

                schichten.AddRange(sorted.Select(x => x.S));
            }
        }

        if (fehler.Count > 0)
        {
            if (fehler.Count >= MaxFehler)
            {
                fehler.Add(new ImportFehler("Weitere Fehler werden nicht angezeigt (Anzeige begrenzt auf {0}).", [MaxFehler.ToString(De)]));
            }

            return new ImportResult([], [], fehler.Select(Deutsch).ToList()) { FehlerTexte = fehler };
        }

        return new ImportResult(neue, schichten, []);
    }

    // ---------- rows ----------

    private static string M(double v) => v.ToString("0.00", De) + " m";

    private static List<(int Line, string[] Fields)> ReadRows(string csv, string header, bool aufschluss, int columns, ZeilenFehler err)
    {
        var result = new List<(int, string[])>();
        var lines = SplitLines(csv.TrimStart('﻿'));
        int headerIndex = lines.FindIndex(l => l.Text.Trim().Length > 0);
        if (headerIndex < 0) { return result; }

        var head = lines[headerIndex];
        string normalised = string.Join(";", SplitFields(head.Text).Select(x => x.Trim()));
        if (!string.Equals(normalised, header, StringComparison.OrdinalIgnoreCase))
        {
            string kopf = aufschluss
                ? "Aufschlüsse, Zeile {0}: Kopfzeile stimmt nicht. Erwartet: {1}"
                : "Schichten, Zeile {0}: Kopfzeile stimmt nicht. Erwartet: {1}";
            err(kopf, head.Number, header);
            return result;
        }

        for (int i = headerIndex + 1; i < lines.Count; i++)
        {
            var l = lines[i];
            if (l.Text.Trim().Length == 0 || l.Text.Trim(';', ' ', '\t').Length == 0) { continue; }
            var fields = SplitFields(l.Text);
            if (fields.Length != columns)
            {
                string spalten = aufschluss
                    ? "Aufschlüsse, Zeile {0}: {1} Spalten erwartet, {2} gefunden (Trennzeichen ist das Semikolon)."
                    : "Schichten, Zeile {0}: {1} Spalten erwartet, {2} gefunden (Trennzeichen ist das Semikolon).";
                err(spalten, l.Number, columns.ToString(De), fields.Length.ToString(De));
                continue;
            }

            result.Add((l.Number, fields));
        }

        return result;
    }

    private static List<(int Number, string Text)> SplitLines(string csv)
    {
        var result = new List<(int, string)>();
        var sb = new StringBuilder();
        int number = 1;
        int startNumber = 1;
        bool inQuotes = false;
        for (int i = 0; i < csv.Length; i++)
        {
            char c = csv[i];
            if (c == '"') { inQuotes = !inQuotes; }
            if (!inQuotes && (c == '\n' || c == '\r'))
            {
                if (c == '\r' && i + 1 < csv.Length && csv[i + 1] == '\n') { i++; }
                result.Add((startNumber, sb.ToString()));
                sb.Clear();
                number++;
                startNumber = number;
                continue;
            }

            if (c == '\n') { number++; }
            sb.Append(c);
        }

        if (sb.Length > 0) { result.Add((startNumber, sb.ToString())); }
        return result;
    }

    private static string[] SplitFields(string line)
    {
        var fields = new List<string>();
        var sb = new StringBuilder();
        bool inQuotes = false;
        for (int i = 0; i < line.Length; i++)
        {
            char c = line[i];
            if (inQuotes)
            {
                if (c == '"' && i + 1 < line.Length && line[i + 1] == '"') { sb.Append('"'); i++; }
                else if (c == '"') { inQuotes = false; }
                else { sb.Append(c); }
            }
            else if (c == '"') { inQuotes = true; }
            else if (c == ';') { fields.Add(sb.ToString()); sb.Clear(); }
            else { sb.Append(c); }
        }

        fields.Add(sb.ToString());
        return fields.ToArray();
    }

    // ---------- numbers ----------

    /// <summary>Accepts decimal comma or point ("12,5", "12.5", "5E-08", "5,0E-08"); not both separators.</summary>
    internal static bool TryNumber(string text, out double value)
    {
        value = 0;
        string t = text.Trim();
        if (t.Length == 0) { return false; }
        if (t.Contains(',') && t.Contains('.')) { return false; }
        t = t.Replace(',', '.');
        return double.TryParse(t, NumberStyles.Float, CultureInfo.InvariantCulture, out value) && double.IsFinite(value);
    }

    // ---------- Aufschluss row ----------

    private static Aufschluss? ParseAufschluss(int line, string[] f, IReadOnlyCollection<Aufschluss> existing,
        Dictionary<string, int> seen, ZeilenFehler err)
    {
        bool ok = true;
        void E(string vorlage, params string[] argumente) { ok = false; err(vorlage, line, argumente); }

        double Pflicht(string text, string spalte)
        {
            if (text.Trim().Length == 0) { E("Aufschlüsse, Zeile {0}: {1} fehlt.", spalte); return double.NaN; }
            if (!TryNumber(text, out double v)) { E("Aufschlüsse, Zeile {0}: {1} {2} ist keine Zahl.", spalte, Q(text.Trim())); return double.NaN; }
            return v;
        }

        string name = f[0].Trim();
        if (name.Length == 0) { E("Aufschlüsse, Zeile {0}: Bezeichnung fehlt."); }
        else if (name.Length > 20) { E("Aufschlüsse, Zeile {0}: Bezeichnung {1} ist länger als 20 Zeichen.", Q(name)); }
        else if (existing.Any(a => string.Equals(a.Bezeichnung, name, StringComparison.OrdinalIgnoreCase)))
        {
            E("Aufschlüsse, Zeile {0}: Bezeichnung {1} existiert bereits im Projekt.", Q(name));
        }
        else if (seen.TryGetValue(name, out int firstLine))
        {
            E("Aufschlüsse, Zeile {0}: Bezeichnung {1} kommt in dieser Datei mehrfach vor (zuerst in Zeile {2}).", Q(name), firstLine.ToString(De));
        }

        AufschlussArt art = AufschlussArt.Kleinrammbohrung;
        switch (f[1].Trim().ToLowerInvariant())
        {
            case "krb": case "kleinrammbohrung": art = AufschlussArt.Kleinrammbohrung; break;
            case "dph": case "rammsondierung": art = AufschlussArt.Rammsondierung; break;
            case "": E("Aufschlüsse, Zeile {0}: Art fehlt (erlaubt: KRB, DPH, Kleinrammbohrung, Rammsondierung)."); break;
            default: E("Aufschlüsse, Zeile {0}: Ungültige Art {1} (erlaubt: KRB, DPH, Kleinrammbohrung, Rammsondierung).", Q(f[1].Trim())); break;
        }

        double rw = Pflicht(f[2], "Rechtswert");
        double hw = Pflicht(f[3], "Hochwert");
        double hoehe = Pflicht(f[4], "Ansatzhoehe_mNHN");
        double endteufe = Pflicht(f[5], "Endteufe_m");
        if (!double.IsNaN(rw) && (rw < 100000 || rw > 1000000)) { E("Aufschlüsse, Zeile {0}: Rechtswert {1} liegt außerhalb des Bereichs für ETRS89 / UTM 32N (EPSG:25832).", f[2].Trim()); }
        if (!double.IsNaN(hw) && (hw < 4000000 || hw > 6500000)) { E("Aufschlüsse, Zeile {0}: Hochwert {1} liegt außerhalb des Bereichs für ETRS89 / UTM 32N (EPSG:25832).", f[3].Trim()); }
        if (!double.IsNaN(hoehe) && (hoehe < -500 || hoehe > 5000)) { E("Aufschlüsse, Zeile {0}: Ansatzhoehe_mNHN {1} ist nicht plausibel.", f[4].Trim()); }
        if (!double.IsNaN(endteufe) && endteufe <= 0) { E("Aufschlüsse, Zeile {0}: Endteufe_m muss größer als 0 sein."); }

        DateOnly datum = default;
        string d = f[6].Trim();
        if (d.Length == 0) { E("Aufschlüsse, Zeile {0}: Datum fehlt (Format TT.MM.JJJJ)."); }
        else if (!DateOnly.TryParseExact(d, "dd.MM.yyyy", CultureInfo.InvariantCulture, DateTimeStyles.None, out datum))
        {
            E("Aufschlüsse, Zeile {0}: Ungültiges Datum {1} (Format TT.MM.JJJJ).", Q(d));
        }
        else if (datum < MinDatum || datum > MaxDatum())
        {
            E("Aufschlüsse, Zeile {0}: Datum {1} liegt außerhalb von 01.01.2000 bis {2}.", Q(d), MaxDatum().ToString("dd.MM.yyyy", CultureInfo.InvariantCulture));
        }

        double? gw = null;
        string g = f[7].Trim();
        if (g.Length > 0)
        {
            if (!TryNumber(g, out double gv)) { E("Aufschlüsse, Zeile {0}: Grundwasser_m {1} ist keine Zahl.", Q(g)); }
            else if (gv < 0) { E("Aufschlüsse, Zeile {0}: Grundwasser_m darf nicht negativ sein."); }
            else if (!double.IsNaN(endteufe) && gv > endteufe + Tol)
            {
                E("Aufschlüsse, Zeile {0}: Grundwasser_m ({1}) liegt tiefer als die Endteufe ({2}).", M(gv), M(endteufe));
            }
            else { gw = gv; }
        }

        if (!ok) { return null; }
        return new Aufschluss
        {
            Bezeichnung = name,
            Art = art,
            Rechtswert = rw,
            Hochwert = hw,
            AnsatzhoeheMNhn = hoehe,
            EndteufeM = endteufe,
            Datum = datum,
            GrundwasserM = gw,
        };
    }

    // ---------- Schicht row ----------

    private static Schicht? ParseSchicht(int line, string[] f, ZeilenFehler err)
    {
        bool ok = true;
        void E(string vorlage, params string[] argumente) { ok = false; err(vorlage, line, argumente); }

        double Pflicht(string text, string spalte)
        {
            if (text.Trim().Length == 0) { E("Schichten, Zeile {0}: {1} fehlt.", spalte); return double.NaN; }
            if (!TryNumber(text, out double v)) { E("Schichten, Zeile {0}: {1} {2} ist keine Zahl.", spalte, Q(text.Trim())); return double.NaN; }
            return v;
        }

        double von = Pflicht(f[1], "Von_m");
        double bis = Pflicht(f[2], "Bis_m");
        if (!double.IsNaN(von) && von < 0) { E("Schichten, Zeile {0}: Von_m darf nicht negativ sein."); }
        if (!double.IsNaN(von) && !double.IsNaN(bis) && bis <= von)
        {
            E("Schichten, Zeile {0}: Bis_m ({1}) muss größer sein als Von_m ({2}).", M(bis), M(von));
        }

        string kurz = f[3].Trim();
        if (kurz.Length == 0) { E("Schichten, Zeile {0}: Kurzzeichen fehlt."); }
        else if (kurz.Length > 40) { E("Schichten, Zeile {0}: Kurzzeichen ist länger als 40 Zeichen."); }

        string beschr = f[4].Trim();
        if (beschr.Length == 0) { E("Schichten, Zeile {0}: Beschreibung fehlt."); }
        else if (beschr.Length > 400) { E("Schichten, Zeile {0}: Beschreibung ist länger als 400 Zeichen."); }

        string? Opt(string v, int max, string spalte)
        {
            string t = v.Trim();
            if (t.Length > max) { E("Schichten, Zeile {0}: {1} ist länger als {2} Zeichen.", spalte, max.ToString(De)); }
            return t.Length == 0 ? null : t;
        }

        string? gruppe = Opt(f[5], 10, "Bodengruppe");
        string? farbe = Opt(f[6], 60, "Farbe");
        string? zustand = Opt(f[7], 60, "Zustand");
        string? homogen = Opt(f[8], 10, "Homogenbereich");

        string? frost = null;
        string fr = f[9].Trim();
        if (fr.Length > 0)
        {
            if (fr.Equals("F1", StringComparison.OrdinalIgnoreCase) || fr.Equals("F2", StringComparison.OrdinalIgnoreCase) || fr.Equals("F3", StringComparison.OrdinalIgnoreCase))
            {
                frost = fr.ToUpperInvariant();
            }
            else
            {
                E("Schichten, Zeile {0}: Ungültige Frostklasse {1} (erlaubt: F1, F2, F3 oder leer).", Q(fr));
            }
        }

        double? kf = null;
        string k = f[10].Trim();
        if (k.Length > 0)
        {
            if (!TryNumber(k, out double kv)) { E("Schichten, Zeile {0}: kf_m_s {1} ist keine Zahl (erlaubt z. B. 5E-08, 5,0E-08, 0,00000005).", Q(k)); }
            else if (kv <= 0) { E("Schichten, Zeile {0}: kf_m_s muss größer als 0 sein."); }
            else { kf = kv; }
        }

        if (!ok) { return null; }
        return new Schicht
        {
            VonM = von,
            BisM = bis,
            Kurzzeichen = kurz,
            Beschreibung = beschr,
            Bodengruppe = gruppe,
            Farbe = farbe,
            Zustand = zustand,
            Homogenbereich = homogen,
            Frostklasse = frost,
            KfWertMs = kf,
        };
    }
}
