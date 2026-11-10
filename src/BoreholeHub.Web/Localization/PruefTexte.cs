using BoreholeHub.Core.Services;
using Microsoft.Extensions.Localization;

namespace BoreholeHub.Web.Localization;

/// <summary>
/// Shows the findings of the data check (Prüfung) in the UI language (D128 3A).
/// Core keeps the German template and its arguments; this class localizes them.
/// Without an English entry the German text is shown.
/// </summary>
public sealed class PruefTexte
{
    /// <summary>
    /// Every German template that DataQualityService can emit. The keys of PruefTexte.en.resx.
    /// Listed here as literals because LocalizationCoverageTests only scans the Web project.
    /// </summary>
    public static readonly IReadOnlyList<string> Vorlagen =
    [
        "Keine Schichten erfasst.",
        "Schicht {0}–{1}: Oberkante liegt nicht über Unterkante.",
        "Lücke {0}–{1} m.",
        "Überlappung {0}–{1} m.",
        "Letzte Schicht endet bei {0} m, Endteufe {1} m.",
        "Endteufe fehlt oder ist 0.",
        "Endteufe {0} m liegt über {1} m (Rahmen GK1).",
        "Probe {0}: Oberkante liegt nicht über Unterkante.",
        "Probe {0} ({1}–{2} m) liegt außerhalb der Bohrung.",
        "Probe {0} ({1}–{2} m) reicht über eine Schichtgrenze.",
        "Koordinaten liegen außerhalb Hessens (ETRS89 / UTM 32N).",
        "Liegt {0} m vom Projektmittelpunkt entfernt.",
        "Ansatzhöhe {0} m NHN ist für Hessen nicht plausibel.",
        "Ansatzhöhe weicht {0} m vom Median des Projekts ab.",
        "Keine Messwerte erfasst.",
        "Negative Schlagzahl bei {0} m.",
        "Sehr hohe Schlagzahl N10 = {0} bei {1} m – Abbruchkriterium prüfen.",
        "Messwertreihe lückenhaft bei {0} m.",
        "kf-Wert fehlt für Schicht {0}–{1} m ({2}) – Versickerung nicht prüfbar.",
    ];

    /// <summary>Message of a finding in the UI language. Falls back to the German Meldung when no template is set.</summary>
    public static string Meldung(IStringLocalizer<PruefTexte> loc, PruefBefund b) =>
        string.IsNullOrEmpty(b.Vorlage) ? b.Meldung : loc[b.Vorlage, b.Argumente.ToArray<object>()].Value;

    /// <summary>Rule name in the UI language. Unknown names are returned unchanged.</summary>
    public static string Regel(IStringLocalizer<PruefTexte> loc, string regel) => regel switch
    {
        "Schichtfolge" or "Endteufe" or "Proben" or "Koordinaten" or "Ansatzhöhe" or "Rammsondierung" or "Versickerung" => loc[regel].Value,
        _ => regel,
    };

    /// <summary>Severity in the UI language: "Error" or "Notice".</summary>
    public static string Stufe(IStringLocalizer<PruefTexte> loc, PruefStufe s) =>
        loc[s == PruefStufe.Fehler ? "Fehler" : "Hinweis"].Value;
}
