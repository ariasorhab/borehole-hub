namespace BoreholeHub.Core.Entities;

/// <summary>A geotechnical project that groups several investigation points.</summary>
public class Project
{
    public int Id { get; set; }

    /// <summary>Project name (required, max 200 characters).</summary>
    public string Name { get; set; } = string.Empty;

    /// <summary>Location (max 100 characters).</summary>
    public string Ort { get; set; } = string.Empty;

    /// <summary>Client (max 200 characters).</summary>
    public string Auftraggeber { get; set; } = string.Empty;

    public string? Beschreibung { get; set; }

    public DateTime ErstelltAm { get; set; }

    public List<Aufschluss> Aufschluesse { get; set; } = new();
}
