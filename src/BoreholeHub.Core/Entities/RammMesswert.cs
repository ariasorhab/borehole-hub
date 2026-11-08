namespace BoreholeHub.Core.Entities;

/// <summary>Dynamic probing blow count for one 10 cm step.</summary>
public class RammMesswert
{
    public int Id { get; set; }

    public int AufschlussId { get; set; }

    public Aufschluss Aufschluss { get; set; } = null!;

    /// <summary>Lower end of the 10 cm step in metres, e.g. 0.1 or 0.2.</summary>
    public double TiefeBisM { get; set; }

    /// <summary>Blows per 10 cm.</summary>
    public int N10 { get; set; }
}
