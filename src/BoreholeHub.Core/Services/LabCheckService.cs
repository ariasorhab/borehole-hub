using BoreholeHub.Core.Entities;

namespace BoreholeHub.Core.Services;

/// <summary>Result of one lab value checked against its class limits.</summary>
/// <param name="Parameter">Parameter name of the lab value.</param>
/// <param name="Wert">Measured value.</param>
/// <param name="Einheit">Unit of the measured value.</param>
/// <param name="EingehalteneKlasse">Strictest class whose limit is met, or null.</param>
/// <param name="Ueberschritten">True when no class limit is met.</param>
/// <param name="UeberschritteneGrenze">Least strict class, when the value exceeds all limits; otherwise null.</param>
public record LabCheckResult(
    string Parameter,
    double Wert,
    string Einheit,
    string? EingehalteneKlasse,
    bool Ueberschritten,
    string? UeberschritteneGrenze);

/// <summary>Checks lab values against class limits (Grenzwerte). Limits are examples unless stated otherwise.</summary>
public static class LabCheckService
{
    /// <summary>Evaluates each lab value against the limits with the same parameter and unit.</summary>
    public static IReadOnlyList<LabCheckResult> Evaluate(
        IEnumerable<Laborwert> werte,
        IEnumerable<Grenzwert> grenzwerte)
    {
        ArgumentNullException.ThrowIfNull(werte);
        ArgumentNullException.ThrowIfNull(grenzwerte);

        var limits = grenzwerte.ToList();
        var results = new List<LabCheckResult>();

        foreach (var wert in werte)
        {
            // Matching limits for this parameter and unit, strictest first.
            var matching = limits
                .Where(g => string.Equals(g.Parameter, wert.Parameter, StringComparison.OrdinalIgnoreCase)
                            && string.Equals(g.Einheit, wert.Einheit, StringComparison.OrdinalIgnoreCase))
                .OrderBy(g => g.Reihenfolge)
                .ToList();

            if (matching.Count == 0)
            {
                results.Add(new LabCheckResult(wert.Parameter, wert.Wert, wert.Einheit, null, false, null));
                continue;
            }

            var firstMet = matching.FirstOrDefault(g => wert.Wert <= g.Wert);
            if (firstMet is not null)
            {
                results.Add(new LabCheckResult(wert.Parameter, wert.Wert, wert.Einheit, firstMet.Klasse, false, null));
            }
            else
            {
                string leastStrict = matching[^1].Klasse;
                results.Add(new LabCheckResult(wert.Parameter, wert.Wert, wert.Einheit, null, true, leastStrict));
            }
        }

        return results;
    }
}
