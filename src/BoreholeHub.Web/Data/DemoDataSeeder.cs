using BoreholeHub.Core.Entities;
using Microsoft.EntityFrameworkCore;

namespace BoreholeHub.Web.Data;

/// <summary>
/// Creates one synthetic demo project (a plot in Schwalmstadt-Treysa) with Kleinrammbohrungen,
/// Rammsondierungen, layers, samples, lab values and example limit values.
/// All data is invented; it only has to look plausible to a geologist.
/// </summary>
public static class DemoDataSeeder
{
    public const string DemoProjectName = "Neubau Einfamilienhaus mit Garage, Schwalmstadt-Treysa (Demo)";

    /// <summary>
    /// Returns the Id of the demo project. If it already exists nothing is changed; otherwise the project
    /// and (when the Grenzwerte table is empty) the example limit values are created.
    /// </summary>
    public static async Task<int> SeedAsync(BoreholeDbContext db, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(db);

        var existingId = await db.Projects
            .Where(p => p.Name == DemoProjectName)
            .Select(p => (int?)p.Id)
            .FirstOrDefaultAsync(ct);
        if (existingId is not null)
        {
            return existingId.Value;
        }

        var project = BuildProject();
        db.Projects.Add(project);

        if (!await db.Grenzwerte.AnyAsync(ct))
        {
            db.Grenzwerte.AddRange(BuildGrenzwerte());
        }

        // One SaveChanges = one transaction: either everything is stored or nothing.
        await db.SaveChangesAsync(ct);
        return project.Id;
    }

    // ---------- Project ----------

    private static Project BuildProject()
    {
        var project = new Project
        {
            Name = DemoProjectName,
            Ort = "Schwalmstadt-Treysa",
            Auftraggeber = "Musterbau GmbH (fiktiv)",
            Beschreibung = "Synthetisches Demoprojekt für eine Baugrunduntersuchung der Geotechnischen Kategorie GK1 (Flachgründung), alle Daten sind erfunden.",
            ErstelltAm = new DateTime(2026, 11, 2),
        };

        // Plot of about 25 m x 20 m around 512870 / 5638720 (EPSG:25832); the ground falls gently towards the south-east.
        // KRB 1 = NW corner, KRB 2 = NE, KRB 3 = centre, KRB 4 = SW, KRB 5 = SE.
        var krb1 = Krb("KRB 1", 512858, 5638730, 215.8, 3.5, null, new DateOnly(2026, 10, 27),
            L(0.00, 1.00, "A, U, s, g'", "Auffüllung: Schluff, sandig, schwach kiesig, vereinzelt Ziegelreste", "UL", "braun, dunkelgrau", "steif", "B1", "F3", 2e-7),
            L(1.00, 1.60, "U, t, s'", "Auenlehm: Schluff, tonig, schwach sandig", "TL", "braun", "steif", "B2", "F3", 5e-8),
            L(1.60, 2.20, "U, t, s'", "Auenlehm: Schluff, tonig, schwach sandig", "TL", "graubraun", "weich", "B2", "F3", 3e-8),
            L(2.20, 3.50, "G, s, u'", "Flusskies: Kies, sandig, schwach schluffig", "GU", "graubraun", "mitteldicht", "B3", "F2", 3e-5));

        var krb2 = Krb("KRB 2", 512882, 5638730, 215.4, 3.5, null, new DateOnly(2026, 10, 27),
            L(0.00, 0.30, "U, h, s'", "Mutterboden: Schluff, humos, schwach sandig", "OH", "dunkelbraun", "weich", "B1", "F2", 1e-6),
            L(0.30, 1.60, "U, t, s'", "Auenlehm: Schluff, tonig, schwach sandig", "TL", "braun", "steif", "B2", "F3", 5e-8),
            L(1.60, 3.50, "G, s, u'", "Flusskies: Kies, sandig, schwach schluffig", "GU", "graubraun", "mitteldicht", "B3", "F2", 3e-5));

        var krb3 = Krb("KRB 3", 512870, 5638720, 215.1, 4.0, 2.9, new DateOnly(2026, 10, 27),
            L(0.00, 0.30, "U, h, s'", "Mutterboden: Schluff, humos, schwach sandig", "OH", "dunkelbraun", "weich", "B1", "F2", 1e-6),
            L(0.30, 1.40, "U, s, t'", "Auenlehm: Schluff, sandig, schwach tonig", "UL", "hellbraun", "steif", "B2", "F3", 1e-7),
            L(1.40, 2.30, "G, s, u'", "Flusskies: Kies, sandig, schwach schluffig", "GU", "graubraun", "mitteldicht", "B3", "F2", 4e-5),
            L(2.30, 4.00, "G, s", "Flusskies: Kies, sandig, sauber", "GW", "grau", "mitteldicht bis dicht", "B3", "F1", 2e-3));

        var krb4 = Krb("KRB 4", 512858, 5638710, 214.9, 3.5, null, new DateOnly(2026, 10, 28),
            L(0.00, 0.70, "A, G, s, u'", "Auffüllung: Kies, sandig, schwach schluffig (Schottertragschicht)", "GU", "grau", "mitteldicht", "B1", "F2", 5e-5),
            L(0.70, 2.00, "U, t, s'", "Auenlehm: Schluff, tonig, schwach sandig", "UL", "braun", "steif", "B2", "F3", 8e-8),
            L(2.00, 3.50, "G, s, u'", "Flusskies: Kies, sandig, schwach schluffig", "GU", "graubraun", "mitteldicht", "B3", "F2", 3e-5));

        var krb5 = Krb("KRB 5", 512882, 5638710, 214.4, 4.0, 2.6, new DateOnly(2026, 10, 28),
            L(0.00, 0.30, "U, h, s'", "Mutterboden: Schluff, humos, schwach sandig", "OH", "dunkelbraun", "weich", "B1", "F2", 1e-6),
            L(0.30, 1.20, "U, t, s'", "Auenlehm: Schluff, tonig, schwach sandig", "TL", "braun", "steif", "B2", "F3", 5e-8),
            L(1.20, 1.90, "U, t", "Auenlehm: Schluff, tonig", "TL", "graubraun", "weich", "B2", "F3", 2e-8),
            L(1.90, 2.80, "G, s, u'", "Flusskies: Kies, sandig, schwach schluffig", "GU", "graubraun", "mitteldicht", "B3", "F2", 5e-5),
            L(2.80, 4.00, "G, s", "Flusskies: Kies, sandig, sauber", "GW", "grau", "mitteldicht bis dicht", "B3", "F1", 1e-3));

        // Samples with lab values (mg/kg). Only KRB 1/1 (fill) clearly exceeds BM-0 for Blei and PAK16.
        krb1.Proben.Add(Probe("KRB 1/1", 0.30, 0.90, 9, 185, 0.7, 35, 62, 21, 0.30, 210, 14.2, 320));
        krb2.Proben.Add(Probe("KRB 2/1", 0.30, 1.20, 5.2, 18, 0.15, 24, 12, 13, 0.05, 48, 0.4, 30));
        krb3.Proben.Add(Probe("KRB 3/1", 0.30, 1.20, 6.1, 21, 0.20, 26, 14, 14, 0.07, 52, 0.6, 40));
        krb3.Proben.Add(Probe("KRB 3/2", 1.40, 2.30, 3.8, 12, 0.10, 18, 8, 10, 0.03, 35, 0.2, 20));
        krb4.Proben.Add(Probe("KRB 4/1", 0.00, 0.70, 6.0, 22, 0.20, 24, 15, 12, 0.06, 78, 1.8, 70));
        krb5.Proben.Add(Probe("KRB 5/1", 0.30, 1.20, 5.6, 19, 0.18, 25, 13, 13, 0.06, 50, 0.5, 35));

        // Heavy dynamic probes next to KRB 1, KRB 3 and KRB 5, down to 4.0 m in 10 cm steps.
        var dph1 = Dph("DPH 1", 512860, 5638731, 215.8, new DateOnly(2026, 10, 27), krb1, seed: 1);
        var dph2 = Dph("DPH 2", 512872, 5638721, 215.1, new DateOnly(2026, 10, 28), krb3, seed: 2);
        var dph3 = Dph("DPH 3", 512884, 5638711, 214.4, new DateOnly(2026, 10, 28), krb5, seed: 3);

        project.Aufschluesse.AddRange([krb1, krb2, krb3, krb4, krb5, dph1, dph2, dph3]);
        return project;
    }

    private static Aufschluss Krb(string name, double rechts, double hoch, double hoehe, double endteufe,
        double? grundwasser, DateOnly datum, params Schicht[] schichten)
    {
        var a = new Aufschluss
        {
            Bezeichnung = name,
            Art = AufschlussArt.Kleinrammbohrung,
            Rechtswert = rechts,
            Hochwert = hoch,
            AnsatzhoeheMNhn = hoehe,
            EndteufeM = endteufe,
            GrundwasserM = grundwasser,
            Datum = datum,
        };
        a.Schichten.AddRange(schichten);
        return a;
    }

    private static Schicht L(double von, double bis, string kurz, string beschreibung, string gruppe, string farbe,
        string zustand, string homogenbereich, string frost, double kf) => new()
    {
        VonM = von,
        BisM = bis,
        Kurzzeichen = kurz,
        Beschreibung = beschreibung,
        Bodengruppe = gruppe,
        Farbe = farbe,
        Zustand = zustand,
        Homogenbereich = homogenbereich,
        Frostklasse = frost,
        KfWertMs = kf,
    };

    // ---------- Samples ----------

    private static readonly string[] LabParameters =
    [
        "Arsen", "Blei", "Cadmium", "Chrom (gesamt)", "Kupfer", "Nickel", "Quecksilber", "Zink", "PAK16", "MKW C10–C40",
    ];

    private static Probe Probe(string name, double von, double bis, params double[] werte)
    {
        var probe = new Probe { Bezeichnung = name, VonM = von, BisM = bis };
        for (int i = 0; i < LabParameters.Length; i++)
        {
            probe.Laborwerte.Add(new Laborwert { Parameter = LabParameters[i], Wert = werte[i], Einheit = "mg/kg" });
        }

        return probe;
    }

    // ---------- Dynamic probing ----------

    private static Aufschluss Dph(string name, double rechts, double hoch, double hoehe, DateOnly datum, Aufschluss nachbar, int seed)
    {
        var a = new Aufschluss
        {
            Bezeichnung = name,
            Art = AufschlussArt.Rammsondierung,
            Rechtswert = rechts,
            Hochwert = hoch,
            AnsatzhoeheMNhn = hoehe,
            EndteufeM = 4.0,
            Datum = datum,
            Bemerkung = "Schwere Rammsonde (DPH) nach DIN EN ISO 22476-2, neben " + nachbar.Bezeichnung + ".",
        };

        for (int i = 1; i <= 40; i++)
        {
            double tiefeBis = Math.Round(i * 0.1, 1);
            // Centre of the 10 cm step decides which neighbouring layer the blow count belongs to.
            double mitte = tiefeBis - 0.05;
            var schicht = nachbar.Schichten.FirstOrDefault(s => s.VonM <= mitte && mitte < s.BisM) ?? nachbar.Schichten[^1];
            a.Messwerte.Add(new RammMesswert { TiefeBisM = tiefeBis, N10 = BlowCount(schicht, mitte, i, seed) });
        }

        return a;
    }

    /// <summary>Deterministic blow count N10 that fits the soil: low in loam, higher and rising with depth in gravel.</summary>
    private static int BlowCount(Schicht schicht, double tiefe, int step, int seed)
    {
        // Small natural variation of -1, 0 or +1, fully deterministic (no Random).
        int variation = ((step * 37 + seed * 11) % 3) - 1;

        int n = schicht.Bodengruppe switch
        {
            "OH" => 2 + (variation > 0 ? 1 : 0),
            "TL" or "UL" => (schicht.Zustand == "weich" ? 2 : 3) + variation,
            "GW" => 12 + (int)Math.Round(2.0 * tiefe) + variation,
            _ when schicht.Homogenbereich == "B1" => 4 + variation,          // fill
            _ => 5 + (int)Math.Round(2.2 * tiefe) + variation,               // silty gravel (GU)
        };

        int min = schicht.Bodengruppe is "TL" or "UL" or "OH" ? 1 : 2;
        int max = schicht.Bodengruppe is "TL" or "UL" or "OH" ? 4 : 20;
        return Math.Clamp(n, min, max);
    }

    // ---------- Limit values ----------

    // Example values only – not the official EBV values.
    // Each row: parameter, then the invented limits for BM-0, BM-0*, BM-F0*, BM-F1, BM-F2, BM-F3 (all in mg/kg).
    private static readonly (string Parameter, double[] Werte)[] ExampleLimits =
    [
        ("Arsen", [8, 12, 20, 30, 45, 60]),
        ("Blei", [30, 60, 100, 160, 250, 400]),
        ("Cadmium", [0.4, 0.8, 1.5, 2.5, 4, 6]),
        ("Chrom (gesamt)", [30, 50, 80, 120, 180, 250]),
        ("Kupfer", [20, 40, 70, 110, 170, 250]),
        ("Nickel", [15, 30, 50, 80, 120, 180]),
        ("Quecksilber", [0.2, 0.4, 0.8, 1.2, 2, 3]),
        ("Zink", [60, 100, 160, 250, 400, 600]),
        ("PAK16", [3, 6, 10, 15, 25, 40]),
        ("MKW C10–C40", [100, 200, 400, 600, 800, 1000]),
    ];

    private static readonly string[] ExampleClasses = ["BM-0", "BM-0*", "BM-F0*", "BM-F1", "BM-F2", "BM-F3"];

    private static IEnumerable<Grenzwert> BuildGrenzwerte()
    {
        foreach (var (parameter, werte) in ExampleLimits)
        {
            for (int i = 0; i < ExampleClasses.Length; i++)
            {
                yield return new Grenzwert
                {
                    Parameter = parameter,
                    Einheit = "mg/kg",
                    Klasse = ExampleClasses[i],
                    Wert = werte[i],
                    Reihenfolge = i + 1,
                    IstBeispiel = true,
                };
            }
        }
    }
}
