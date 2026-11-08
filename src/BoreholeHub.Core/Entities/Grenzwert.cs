namespace BoreholeHub.Core.Entities;

/// <summary>One class limit for a parameter. Stored values are examples unless IstBeispiel is false.</summary>
public class Grenzwert
{
    public int Id { get; set; }

    /// <summary>Parameter name (max 60 characters).</summary>
    public string Parameter { get; set; } = string.Empty;

    /// <summary>Unit (max 20 characters).</summary>
    public string Einheit { get; set; } = string.Empty;

    /// <summary>Class name, e.g. "Z0" (max 20 characters).</summary>
    public string Klasse { get; set; } = string.Empty;

    /// <summary>Limit value in <see cref="Einheit"/>.</summary>
    public double Wert { get; set; }

    /// <summary>Order of the classes, from strictest (1) to least strict.</summary>
    public int Reihenfolge { get; set; }

    /// <summary>True when the value is an example and not an official limit.</summary>
    public bool IstBeispiel { get; set; } = true;
}
