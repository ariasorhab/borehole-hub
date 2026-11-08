namespace BoreholeHub.Core.Entities;

/// <summary>A sample taken from an Aufschluss, with its lab results.</summary>
public class Probe
{
    public int Id { get; set; }

    public int AufschlussId { get; set; }

    public Aufschluss Aufschluss { get; set; } = null!;

    /// <summary>Sample designation, e.g. "KRB 1/1" (max 20 characters).</summary>
    public string Bezeichnung { get; set; } = string.Empty;

    /// <summary>Top of the sample in metres below ground surface.</summary>
    public double VonM { get; set; }

    /// <summary>Bottom of the sample in metres below ground surface.</summary>
    public double BisM { get; set; }

    public List<Laborwert> Laborwerte { get; set; } = new();
}
