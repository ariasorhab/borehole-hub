namespace BoreholeHub.Core.Entities;

/// <summary>A soil or rock layer within an Aufschluss.</summary>
public class Schicht
{
    public int Id { get; set; }

    public int AufschlussId { get; set; }

    public Aufschluss Aufschluss { get; set; } = null!;

    /// <summary>Top of the layer in metres below ground surface.</summary>
    public double VonM { get; set; }

    /// <summary>Bottom of the layer in metres below ground surface.</summary>
    public double BisM { get; set; }

    /// <summary>Main and secondary component code, e.g. "U, s', t" (max 40 characters).</summary>
    public string Kurzzeichen { get; set; } = string.Empty;

    /// <summary>Description (max 400 characters).</summary>
    public string Beschreibung { get; set; } = string.Empty;

    /// <summary>Soil group per DIN 18196, e.g. "UL" (max 10 characters).</summary>
    public string? Bodengruppe { get; set; }

    /// <summary>Colour (max 60 characters).</summary>
    public string? Farbe { get; set; }

    /// <summary>Consistency or density, e.g. "steif" or "mitteldicht" (max 60 characters).</summary>
    public string? Zustand { get; set; }

    /// <summary>Homogeneous area, e.g. "B1" (max 10 characters).</summary>
    public string? Homogenbereich { get; set; }

    /// <summary>Frost susceptibility class: "F1", "F2" or "F3" (max 2 characters).</summary>
    public string? Frostklasse { get; set; }

    /// <summary>Hydraulic conductivity in m/s.</summary>
    public double? KfWertMs { get; set; }
}
