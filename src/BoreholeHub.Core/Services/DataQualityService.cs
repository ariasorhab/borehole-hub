using System.Globalization;
using BoreholeHub.Core.Entities;

namespace BoreholeHub.Core.Services;

/// <summary>Severity of a finding. <c>Fehler</c> sorts before <c>Hinweis</c>.</summary>
public enum PruefStufe { Fehler, Hinweis }

/// <summary>One finding of the data quality check (Prüfung).</summary>
/// <param name="Regel">Name of the rule, e.g. "Schichtfolge".</param>
/// <param name="Stufe">Severity of the finding.</param>
/// <param name="AufschlussId">Id of the checked Aufschluss.</param>
/// <param name="Aufschluss">Designation of the checked Aufschluss.</param>
/// <param name="Meldung">Message in German.</param>
public record PruefBefund(string Regel, PruefStufe Stufe, int AufschlussId, string Aufschluss, string Meldung)
{
    /// <summary>German composite-format template of <see cref="Meldung"/>, e.g. "Lücke {0}–{1} m.". Empty if not set.</summary>
    public string Vorlage { get; init; } = string.Empty;

    /// <summary>Arguments of <see cref="Vorlage"/>, already formatted in de-DE, e.g. "1,00".</summary>
    public IReadOnlyList<string> Argumente { get; init; } = Array.Empty<string>();

    /// <summary>Equality over the five positional members only; <see cref="Vorlage"/> and <see cref="Argumente"/> are not compared.</summary>
    public virtual bool Equals(PruefBefund? other) =>
        other is not null
        && Regel == other.Regel
        && Stufe == other.Stufe
        && AufschlussId == other.AufschlussId
        && Aufschluss == other.Aufschluss
        && Meldung == other.Meldung;

    public override int GetHashCode() => HashCode.Combine(Regel, Stufe, AufschlussId, Aufschluss, Meldung);
}

/// <summary>
/// Automatic plausibility check of investigation points with fixed rules (no configuration).
/// Expects the layers, samples and blow counts of every Aufschluss to be loaded.
/// </summary>
public static class DataQualityService
{
    public const double Toleranz = 0.005;            // m
    public const double MaxEndteufeM = 4.0;
    public const double MaxAbstandM = 500.0;
    public const double MaxHoehenabweichungM = 10.0;
    public const int MaxN10 = 50;

    // Plausibility ranges: ETRS89 / UTM 32N for Hessen, heights in m NHN.
    private const double RechtswertMin = 400000, RechtswertMax = 600000;
    private const double HochwertMin = 5450000, HochwertMax = 5750000;
    private const double HoeheMin = 50, HoeheMax = 1000;

    private const double RammSchritt = 0.10;         // m, step between two blow counts
    private const int MinKoordinaten = 2;            // points needed for the distance check
    private const int MinHoehen = 3;                 // heights needed for the median check

    private static readonly CultureInfo De = CultureInfo.GetCultureInfo("de-DE");

    private static readonly HashSet<string> VersickerungsBodengruppen = new(StringComparer.OrdinalIgnoreCase)
    {
        "SE", "SW", "SI", "SU", "ST", "GE", "GW", "GI", "GU", "GT",
    };

    /// <summary>Rule names; index 0 is R1.</summary>
    private static readonly string[] Regeln = ["Schichtfolge", "Endteufe", "Proben", "Koordinaten", "Ansatzhöhe", "Rammsondierung", "Versickerung"];

    /// <summary>Project-wide values that some rules compare against.</summary>
    private sealed record Kontext(double? MittelRechtswert, double? MittelHochwert, double? MedianHoehe);

    /// <summary>Reports a finding: rule, severity, German template and its already formatted arguments.</summary>
    private delegate void Melder(int regel, PruefStufe stufe, string vorlage, params string[] argumente);

    /// <summary>
    /// Runs all rules over the given Aufschlüsse. Order: Stufe (Fehler first), then Aufschluss.Bezeichnung (ordinal),
    /// then rule R1 to R7, then the order in which the findings were found.
    /// </summary>
    public static IReadOnlyList<PruefBefund> Pruefen(IReadOnlyList<Aufschluss> aufschluesse)
    {
        ArgumentNullException.ThrowIfNull(aufschluesse);

        var kontext = BuildKontext(aufschluesse);
        var treffer = new List<(int Regel, PruefBefund Befund)>();

        foreach (var a in aufschluesse)
        {
            string name = a.Bezeichnung ?? string.Empty;
            void Add(int regel, PruefStufe stufe, string vorlage, params string[] argumente) =>
                treffer.Add((regel, new PruefBefund(Regeln[regel - 1], stufe, a.Id, name, string.Format(De, vorlage, argumente))
                {
                    Vorlage = vorlage,
                    Argumente = argumente,
                }));

            Schichtfolge(a, Add);
            Endteufe(a, Add);
            Proben(a, Add);
            Koordinaten(a, kontext, Add);
            Ansatzhoehe(a, kontext, Add);
            Rammsondierung(a, Add);
            Versickerung(a, Add);
        }

        return treffer
            .OrderBy(t => t.Befund.Stufe)
            .ThenBy(t => t.Befund.Aufschluss, StringComparer.Ordinal)
            .ThenBy(t => t.Regel)
            .Select(t => t.Befund)
            .ToList();
    }

    // ---------- project-wide context ----------

    private static Kontext BuildKontext(IReadOnlyList<Aufschluss> aufschluesse)
    {
        var koordinaten = aufschluesse.Where(KoordinatenPlausibel).ToList();
        double? mittelRw = null, mittelHw = null;
        if (koordinaten.Count >= MinKoordinaten)
        {
            mittelRw = koordinaten.Average(a => a.Rechtswert);
            mittelHw = koordinaten.Average(a => a.Hochwert);
        }

        var hoehen = aufschluesse.Where(HoehePlausibel).Select(a => a.AnsatzhoeheMNhn).OrderBy(h => h).ToList();
        double? median = hoehen.Count >= MinHoehen ? Median(hoehen) : null;
        return new Kontext(mittelRw, mittelHw, median);
    }

    private static bool KoordinatenPlausibel(Aufschluss a) =>
        a.Rechtswert >= RechtswertMin && a.Rechtswert <= RechtswertMax
        && a.Hochwert >= HochwertMin && a.Hochwert <= HochwertMax;

    private static bool HoehePlausibel(Aufschluss a) => a.AnsatzhoeheMNhn >= HoeheMin && a.AnsatzhoeheMNhn <= HoeheMax;

    /// <summary>Median of an already sorted, non-empty list.</summary>
    private static double Median(IReadOnlyList<double> sortiert)
    {
        int n = sortiert.Count;
        return n % 2 == 1 ? sortiert[n / 2] : (sortiert[n / 2 - 1] + sortiert[n / 2]) / 2;
    }

    private static string M(double wert) => wert.ToString("0.00", De);

    // ---------- R1 Schichtfolge (Kleinrammbohrung) ----------

    private static void Schichtfolge(Aufschluss a, Melder melde)
    {
        if (a.Art != AufschlussArt.Kleinrammbohrung) { return; }
        if (a.Schichten.Count == 0)
        {
            melde(1, PruefStufe.Fehler, "Keine Schichten erfasst.");
            return;
        }

        var s = a.Schichten.OrderBy(x => x.VonM).ToList();
        for (int i = 0; i < s.Count; i++)
        {
            if (s[i].VonM >= s[i].BisM)
            {
                melde(1, PruefStufe.Fehler, "Schicht {0}–{1}: Oberkante liegt nicht über Unterkante.", M(s[i].VonM), M(s[i].BisM));
            }

            if (i == 0)
            {
                if (s[0].VonM > Toleranz)
                {
                    melde(1, PruefStufe.Fehler, "Lücke {0}–{1} m.", M(0), M(s[0].VonM));
                }
            }
            else
            {
                var prev = s[i - 1];
                var next = s[i];
                if (next.VonM > prev.BisM + Toleranz)
                {
                    melde(1, PruefStufe.Fehler, "Lücke {0}–{1} m.", M(prev.BisM), M(next.VonM));
                }
                else if (next.VonM < prev.BisM - Toleranz)
                {
                    melde(1, PruefStufe.Fehler, "Überlappung {0}–{1} m.", M(next.VonM), M(prev.BisM));
                }
            }
        }

        var last = s[^1];
        if (Math.Abs(last.BisM - a.EndteufeM) > Toleranz)
        {
            melde(1, PruefStufe.Hinweis, "Letzte Schicht endet bei {0} m, Endteufe {1} m.", M(last.BisM), M(a.EndteufeM));
        }
    }

    // ---------- R2 Endteufe ----------

    private static void Endteufe(Aufschluss a, Melder melde)
    {
        if (a.EndteufeM <= 0)
        {
            melde(2, PruefStufe.Fehler, "Endteufe fehlt oder ist 0.");
        }
        else if (a.EndteufeM > MaxEndteufeM)
        {
            melde(2, PruefStufe.Hinweis, "Endteufe {0} m liegt über {1} m (Rahmen GK1).", M(a.EndteufeM), M(MaxEndteufeM));
        }
    }

    // ---------- R3 Proben ----------

    private static void Proben(Aufschluss a, Melder melde)
    {
        foreach (var p in a.Proben)
        {
            bool fehler = false;
            if (p.VonM >= p.BisM)
            {
                melde(3, PruefStufe.Fehler, "Probe {0}: Oberkante liegt nicht über Unterkante.", p.Bezeichnung);
                fehler = true;
            }

            if (p.VonM < -Toleranz || p.BisM > a.EndteufeM + Toleranz)
            {
                melde(3, PruefStufe.Fehler, "Probe {0} ({1}–{2} m) liegt außerhalb der Bohrung.", p.Bezeichnung, M(p.VonM), M(p.BisM));
                fehler = true;
            }

            if (!fehler && a.Art == AufschlussArt.Kleinrammbohrung && a.Schichten.Count > 0
                && !a.Schichten.Any(s => s.VonM - Toleranz <= p.VonM && p.BisM <= s.BisM + Toleranz))
            {
                melde(3, PruefStufe.Hinweis, "Probe {0} ({1}–{2} m) reicht über eine Schichtgrenze.", p.Bezeichnung, M(p.VonM), M(p.BisM));
            }
        }
    }

    // ---------- R4 Koordinaten ----------

    private static void Koordinaten(Aufschluss a, Kontext k, Melder melde)
    {
        if (!KoordinatenPlausibel(a))
        {
            melde(4, PruefStufe.Fehler, "Koordinaten liegen außerhalb Hessens (ETRS89 / UTM 32N).");
            return;
        }

        if (k.MittelRechtswert is double mr && k.MittelHochwert is double mh)
        {
            double d = Math.Sqrt(Math.Pow(a.Rechtswert - mr, 2) + Math.Pow(a.Hochwert - mh, 2));
            if (d > MaxAbstandM)
            {
                string meter = Math.Round(d, MidpointRounding.AwayFromZero).ToString("0", De);
                melde(4, PruefStufe.Hinweis, "Liegt {0} m vom Projektmittelpunkt entfernt.", meter);
            }
        }
    }

    // ---------- R5 Ansatzhöhe ----------

    private static void Ansatzhoehe(Aufschluss a, Kontext k, Melder melde)
    {
        if (!HoehePlausibel(a))
        {
            melde(5, PruefStufe.Fehler, "Ansatzhöhe {0} m NHN ist für Hessen nicht plausibel.", M(a.AnsatzhoeheMNhn));
            return;
        }

        if (k.MedianHoehe is double median)
        {
            double diff = Math.Abs(a.AnsatzhoeheMNhn - median);
            if (diff > MaxHoehenabweichungM)
            {
                melde(5, PruefStufe.Hinweis, "Ansatzhöhe weicht {0} m vom Median des Projekts ab.", M(diff));
            }
        }
    }

    // ---------- R6 Rammsondierung ----------

    private static void Rammsondierung(Aufschluss a, Melder melde)
    {
        if (a.Art != AufschlussArt.Rammsondierung) { return; }
        if (a.Messwerte.Count == 0)
        {
            melde(6, PruefStufe.Fehler, "Keine Messwerte erfasst.");
            return;
        }

        var werte = a.Messwerte.OrderBy(w => w.TiefeBisM).ToList();
        foreach (var w in werte)
        {
            if (w.N10 < 0)
            {
                melde(6, PruefStufe.Fehler, "Negative Schlagzahl bei {0} m.", M(w.TiefeBisM));
            }

            if (w.N10 > MaxN10)
            {
                melde(6, PruefStufe.Hinweis, "Sehr hohe Schlagzahl N10 = {0} bei {1} m – Abbruchkriterium prüfen.", w.N10.ToString(De), M(w.TiefeBisM));
            }
        }

        // The series starts at 0,10 m and goes on in 0,10 m steps. Each place is reported once.
        var gemeldet = new HashSet<double>();
        for (int i = 0; i < werte.Count; i++)
        {
            double schritt = i == 0 ? werte[0].TiefeBisM : werte[i].TiefeBisM - werte[i - 1].TiefeBisM;
            if (Math.Abs(schritt - RammSchritt) > Toleranz && gemeldet.Add(werte[i].TiefeBisM))
            {
                melde(6, PruefStufe.Hinweis, "Messwertreihe lückenhaft bei {0} m.", M(werte[i].TiefeBisM));
            }
        }
    }

    // ---------- R7 Versickerung ----------

    private static void Versickerung(Aufschluss a, Melder melde)
    {
        if (a.Art != AufschlussArt.Kleinrammbohrung) { return; }

        foreach (var s in a.Schichten.OrderBy(x => x.VonM))
        {
            string? bodengruppe = s.Bodengruppe?.Trim();
            if (!string.IsNullOrEmpty(bodengruppe) && VersickerungsBodengruppen.Contains(bodengruppe) && s.KfWertMs is null)
            {
                melde(7, PruefStufe.Hinweis, "kf-Wert fehlt für Schicht {0}–{1} m ({2}) – Versickerung nicht prüfbar.", M(s.VonM), M(s.BisM), bodengruppe);
            }
        }
    }
}
