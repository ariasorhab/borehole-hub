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
public record ImportResult(IReadOnlyList<Aufschluss> NeueAufschluesse, IReadOnlyList<Schicht> NeueSchichten, IReadOnlyList<string> Fehler);

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

    private const string FileA = "Aufschlüsse";
    private const string FileS = "Schichten";

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

    public ImportResult Parse(string? aufschluesseCsv, string? schichtenCsv, IReadOnlyCollection<Aufschluss> existing)
    {
        ArgumentNullException.ThrowIfNull(existing);
        var fehler = new List<string>();
        void Err(string file, int line, string message)
        {
            if (fehler.Count < MaxFehler) { fehler.Add($"{file}, Zeile {line}: {message}"); }
        }

        bool hasA = !string.IsNullOrWhiteSpace(aufschluesseCsv);
        bool hasS = !string.IsNullOrWhiteSpace(schichtenCsv);
        if (!hasA && !hasS)
        {
            return new ImportResult([], [], ["Es wurde keine Datei ausgewählt oder die Datei ist leer."]);
        }

        // ---------- Aufschlüsse ----------
        var neue = new List<Aufschluss>();
        var lineOfNew = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);
        if (hasA)
        {
            var rows = ReadRows(aufschluesseCsv!, AufschluesseHeader, FileA, 8, Err);
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
            var rows = ReadRows(schichtenCsv!, SchichtenHeader, FileS, 11, Err);
            var perAufschluss = new Dictionary<string, List<(int Line, Schicht S)>>(StringComparer.OrdinalIgnoreCase);
            foreach (var (line, f) in rows)
            {
                string name = f[0].Trim();
                if (name.Length == 0)
                {
                    Err(FileS, line, "Bezeichnung fehlt.");
                    continue;
                }

                var neu = neue.FirstOrDefault(a => string.Equals(a.Bezeichnung, name, StringComparison.OrdinalIgnoreCase));
                var vorhanden = existing.FirstOrDefault(a => string.Equals(a.Bezeichnung, name, StringComparison.OrdinalIgnoreCase));
                var ziel = neu ?? vorhanden;
                if (ziel is null)
                {
                    Err(FileS, line, $"Aufschluss \"{name}\" ist weder in der Aufschlüsse-Datei noch im Projekt vorhanden.");
                    continue;
                }

                if (neu is null && vorhanden!.Schichten.Count > 0)
                {
                    Err(FileS, line, $"Aufschluss \"{name}\" hat im Projekt bereits Schichten; sie können nicht nachträglich ergänzt werden.");
                    continue;
                }

                if (ziel.Art == AufschlussArt.Rammsondierung)
                {
                    Err(FileS, line, $"Rammsondierung \"{ziel.Bezeichnung}\" kann keine Schichten haben.");
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
                        Err(FileS, line, $"Die erste Schicht von {name} beginnt bei {M(s.VonM)}, nicht bei 0,00 m.");
                    }
                    else if (!first && s.VonM > cursor + Tol)
                    {
                        Err(FileS, line, $"Lücke zwischen {M(cursor)} und {M(s.VonM)} bei {name}");
                    }
                    else if (!first && s.VonM < cursor - Tol)
                    {
                        Err(FileS, line, $"Überlappung zwischen {M(s.VonM)} und {M(cursor)} bei {name}");
                    }

                    first = false;
                    cursor = s.BisM;
                }

                if (Math.Abs(cursor - ziel.EndteufeM) > Tol)
                {
                    Err(FileS, sorted[^1].Line, $"Die letzte Schicht von {name} endet bei {M(cursor)}, die Endteufe ist {M(ziel.EndteufeM)}.");
                }

                schichten.AddRange(sorted.Select(x => x.S));
            }
        }

        if (fehler.Count > 0)
        {
            if (fehler.Count >= MaxFehler) { fehler.Add($"Weitere Fehler werden nicht angezeigt (Anzeige begrenzt auf {MaxFehler})."); }
            return new ImportResult([], [], fehler);
        }

        return new ImportResult(neue, schichten, []);
    }

    // ---------- rows ----------

    private static string M(double v) => v.ToString("0.00", De) + " m";

    private static List<(int Line, string[] Fields)> ReadRows(string csv, string header, string file, int columns, Action<string, int, string> err)
    {
        var result = new List<(int, string[])>();
        var lines = SplitLines(csv.TrimStart('﻿'));
        int headerIndex = lines.FindIndex(l => l.Text.Trim().Length > 0);
        if (headerIndex < 0) { return result; }

        var head = lines[headerIndex];
        string normalised = string.Join(";", SplitFields(head.Text).Select(x => x.Trim()));
        if (!string.Equals(normalised, header, StringComparison.OrdinalIgnoreCase))
        {
            err(file, head.Number, $"Kopfzeile stimmt nicht. Erwartet: {header}");
            return result;
        }

        for (int i = headerIndex + 1; i < lines.Count; i++)
        {
            var l = lines[i];
            if (l.Text.Trim().Length == 0 || l.Text.Trim(';', ' ', '\t').Length == 0) { continue; }
            var fields = SplitFields(l.Text);
            if (fields.Length != columns)
            {
                err(file, l.Number, $"{columns} Spalten erwartet, {fields.Length} gefunden (Trennzeichen ist das Semikolon).");
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
        Dictionary<string, int> seen, Action<string, int, string> err)
    {
        bool ok = true;
        void E(string message) { ok = false; err(FileA, line, message); }

        string name = f[0].Trim();
        if (name.Length == 0) { E("Bezeichnung fehlt."); }
        else if (name.Length > 20) { E($"Bezeichnung \"{name}\" ist länger als 20 Zeichen."); }
        else if (existing.Any(a => string.Equals(a.Bezeichnung, name, StringComparison.OrdinalIgnoreCase)))
        {
            E($"Bezeichnung \"{name}\" existiert bereits im Projekt.");
        }
        else if (seen.TryGetValue(name, out int firstLine))
        {
            E($"Bezeichnung \"{name}\" kommt in dieser Datei mehrfach vor (zuerst in Zeile {firstLine}).");
        }

        AufschlussArt art = AufschlussArt.Kleinrammbohrung;
        switch (f[1].Trim().ToLowerInvariant())
        {
            case "krb": case "kleinrammbohrung": art = AufschlussArt.Kleinrammbohrung; break;
            case "dph": case "rammsondierung": art = AufschlussArt.Rammsondierung; break;
            case "": E("Art fehlt (erlaubt: KRB, DPH, Kleinrammbohrung, Rammsondierung)."); break;
            default: E($"Ungültige Art \"{f[1].Trim()}\" (erlaubt: KRB, DPH, Kleinrammbohrung, Rammsondierung)."); break;
        }

        double rw = Required(f[2], "Rechtswert", E);
        double hw = Required(f[3], "Hochwert", E);
        double hoehe = Required(f[4], "Ansatzhoehe_mNHN", E);
        double endteufe = Required(f[5], "Endteufe_m", E);
        if (!double.IsNaN(rw) && (rw < 100000 || rw > 1000000)) { E($"Rechtswert {f[2].Trim()} liegt außerhalb des Bereichs für ETRS89 / UTM 32N (EPSG:25832)."); }
        if (!double.IsNaN(hw) && (hw < 4000000 || hw > 6500000)) { E($"Hochwert {f[3].Trim()} liegt außerhalb des Bereichs für ETRS89 / UTM 32N (EPSG:25832)."); }
        if (!double.IsNaN(hoehe) && (hoehe < -500 || hoehe > 5000)) { E($"Ansatzhoehe_mNHN {f[4].Trim()} ist nicht plausibel."); }
        if (!double.IsNaN(endteufe) && endteufe <= 0) { E("Endteufe_m muss größer als 0 sein."); }

        DateOnly datum = default;
        string d = f[6].Trim();
        if (d.Length == 0) { E("Datum fehlt (Format TT.MM.JJJJ)."); }
        else if (!DateOnly.TryParseExact(d, "dd.MM.yyyy", CultureInfo.InvariantCulture, DateTimeStyles.None, out datum))
        {
            E($"Ungültiges Datum \"{d}\" (Format TT.MM.JJJJ).");
        }

        double? gw = null;
        string g = f[7].Trim();
        if (g.Length > 0)
        {
            if (!TryNumber(g, out double gv)) { E($"Grundwasser_m \"{g}\" ist keine Zahl."); }
            else if (gv < 0) { E("Grundwasser_m darf nicht negativ sein."); }
            else if (!double.IsNaN(endteufe) && gv > endteufe + Tol) { E($"Grundwasser_m ({M(gv)}) liegt tiefer als die Endteufe ({M(endteufe)})."); }
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

    /// <summary>Returns the number, or NaN after reporting an error.</summary>
    private static double Required(string text, string column, Action<string> error)
    {
        if (text.Trim().Length == 0) { error($"{column} fehlt."); return double.NaN; }
        if (!TryNumber(text, out double v)) { error($"{column} \"{text.Trim()}\" ist keine Zahl."); return double.NaN; }
        return v;
    }

    // ---------- Schicht row ----------

    private static Schicht? ParseSchicht(int line, string[] f, Action<string, int, string> err)
    {
        bool ok = true;
        void E(string message) { ok = false; err(FileS, line, message); }

        double von = Required(f[1], "Von_m", E);
        double bis = Required(f[2], "Bis_m", E);
        if (!double.IsNaN(von) && von < 0) { E("Von_m darf nicht negativ sein."); }
        if (!double.IsNaN(von) && !double.IsNaN(bis) && bis <= von) { E($"Bis_m ({M(bis)}) muss größer sein als Von_m ({M(von)})."); }

        string kurz = f[3].Trim();
        if (kurz.Length == 0) { E("Kurzzeichen fehlt."); }
        else if (kurz.Length > 40) { E("Kurzzeichen ist länger als 40 Zeichen."); }

        string beschr = f[4].Trim();
        if (beschr.Length == 0) { E("Beschreibung fehlt."); }
        else if (beschr.Length > 400) { E("Beschreibung ist länger als 400 Zeichen."); }

        string? Opt(string v, int max, string column)
        {
            string t = v.Trim();
            if (t.Length > max) { E($"{column} ist länger als {max} Zeichen."); }
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
                E($"Ungültige Frostklasse \"{fr}\" (erlaubt: F1, F2, F3 oder leer).");
            }
        }

        double? kf = null;
        string k = f[10].Trim();
        if (k.Length > 0)
        {
            if (!TryNumber(k, out double kv)) { E($"kf_m_s \"{k}\" ist keine Zahl (erlaubt z. B. 5E-08, 5,0E-08, 0,00000005)."); }
            else if (kv <= 0) { E("kf_m_s muss größer als 0 sein."); }
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
