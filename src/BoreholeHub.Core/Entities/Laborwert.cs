namespace BoreholeHub.Core.Entities;

/// <summary>One lab result for a sample.</summary>
public class Laborwert
{
    public int Id { get; set; }

    public int ProbeId { get; set; }

    public Probe Probe { get; set; } = null!;

    /// <summary>Parameter name (max 60 characters).</summary>
    public string Parameter { get; set; } = string.Empty;

    public double Wert { get; set; }

    /// <summary>Unit (max 20 characters).</summary>
    public string Einheit { get; set; } = string.Empty;
}
