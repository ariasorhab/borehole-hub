namespace BoreholeHub.Core.Entities;

/// <summary>One investigation point: a Kleinrammbohrung or a Rammsondierung.</summary>
public class Aufschluss
{
    public int Id { get; set; }

    public int ProjectId { get; set; }

    public Project Project { get; set; } = null!;

    /// <summary>Designation, e.g. "KRB 1" or "DPH 1" (required, max 20 characters).</summary>
    public string Bezeichnung { get; set; } = string.Empty;

    public AufschlussArt Art { get; set; }

    /// <summary>Easting in metres, EPSG:25832.</summary>
    public double Rechtswert { get; set; }

    /// <summary>Northing in metres, EPSG:25832.</summary>
    public double Hochwert { get; set; }

    /// <summary>Ground level in metres above sea level (mNHN).</summary>
    public double AnsatzhoeheMNhn { get; set; }

    /// <summary>Final depth in metres below ground surface.</summary>
    public double EndteufeM { get; set; }

    public DateOnly Datum { get; set; }

    /// <summary>Depth below ground surface where groundwater was met; null if not met.</summary>
    public double? GrundwasserM { get; set; }

    public string? Bemerkung { get; set; }

    public List<Schicht> Schichten { get; set; } = new();

    public List<Probe> Proben { get; set; } = new();

    public List<RammMesswert> Messwerte { get; set; } = new();
}
