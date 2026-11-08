using System.Globalization;
using BoreholeHub.Core.Entities;
using BoreholeHub.Web.Data;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using Microsoft.EntityFrameworkCore;

namespace BoreholeHub.Web.Pages.Projekte;

public class DetailsModel(BoreholeDbContext db) : PageModel
{
    /// <summary>One row of the Aufschluss table.</summary>
    public record AufschlussZeile(
        int Id,
        string Bezeichnung,
        AufschlussArt Art,
        DateOnly Datum,
        double AnsatzhoeheMNhn,
        double EndteufeM,
        double? GrundwasserM,
        double Rechtswert,
        double Hochwert);

    public Project Projekt { get; private set; } = null!;

    public IReadOnlyList<AufschlussZeile> Aufschluesse { get; private set; } = [];

    public int Gesamt => Aufschluesse.Count;

    public int Kleinrammbohrungen => Aufschluesse.Count(a => a.Art == AufschlussArt.Kleinrammbohrung);

    public int Rammsondierungen => Aufschluesse.Count(a => a.Art == AufschlussArt.Rammsondierung);

    public int MitGrundwasser => Aufschluesse.Count(a => a.GrundwasserM is not null);

    /// <summary>Largest final depth, or null when the project has no Aufschlüsse.</summary>
    public double? MaxEndteufeM => Aufschluesse.Count == 0 ? null : Aufschluesse.Max(a => a.EndteufeM);

    public async Task<IActionResult> OnGetAsync(int id)
    {
        var projekt = await db.Projects.AsNoTracking().FirstOrDefaultAsync(p => p.Id == id);
        if (projekt is null)
        {
            return NotFound();
        }

        Projekt = projekt;
        Aufschluesse = await db.Aufschluesse
            .AsNoTracking()
            .Where(a => a.ProjectId == id)
            .OrderBy(a => a.Art).ThenBy(a => a.Id)
            .Select(a => new AufschlussZeile(
                a.Id, a.Bezeichnung, a.Art, a.Datum, a.AnsatzhoeheMNhn, a.EndteufeM, a.GrundwasserM, a.Rechtswert, a.Hochwert))
            .ToListAsync();
        return Page();
    }

    /// <summary>Formats a depth or height with one decimal and the German decimal comma, e.g. "4,0 m".</summary>
    public static string Meter(double value) => value.ToString("0.0", CultureInfo.GetCultureInfo("de-DE")) + " m";

    /// <summary>Formats a coordinate without decimals and without a thousands separator.</summary>
    public static string Koordinate(double value) => value.ToString("0", CultureInfo.InvariantCulture);
}
