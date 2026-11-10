using System.Globalization;
using BoreholeHub.Core.Entities;
using BoreholeHub.Core.Services;
using BoreholeHub.Web.Data;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Localization;

namespace BoreholeHub.Web.Pages.Projekte;

public class KarteModel(BoreholeDbContext db, IStringLocalizer<KarteModel> loc) : PageModel
{
    private static readonly CultureInfo De = CultureInfo.GetCultureInfo("de-DE");

    /// <summary>One entry of the side panel list.</summary>
    public record ListeneintragZeile(int Id, string Bezeichnung, AufschlussArt Art, double EndteufeM, double? GrundwasserM);

    public Project Projekt { get; private set; } = null!;

    public IReadOnlyList<ListeneintragZeile> Kleinrammbohrungen { get; private set; } = [];

    public IReadOnlyList<ListeneintragZeile> Rammsondierungen { get; private set; } = [];

    public async Task<IActionResult> OnGetAsync(int id)
    {
        var projekt = await db.Projects.AsNoTracking().FirstOrDefaultAsync(p => p.Id == id);
        if (projekt is null)
        {
            return NotFound();
        }

        Projekt = projekt;
        var zeilen = await db.Aufschluesse
            .AsNoTracking()
            .Where(a => a.ProjectId == id)
            .OrderBy(a => a.Id)
            .Select(a => new ListeneintragZeile(a.Id, a.Bezeichnung, a.Art, a.EndteufeM, a.GrundwasserM))
            .ToListAsync();
        Kleinrammbohrungen = zeilen.Where(z => z.Art == AufschlussArt.Kleinrammbohrung).ToList();
        Rammsondierungen = zeilen.Where(z => z.Art == AufschlussArt.Rammsondierung).ToList();
        return Page();
    }

    /// <summary>GeoJSON FeatureCollection of all Aufschlüsse; coordinates are converted to WGS84 on the server.</summary>
    public async Task<IActionResult> OnGetPunkteAsync(int id)
    {
        if (!await db.Projects.AnyAsync(p => p.Id == id))
        {
            return NotFound();
        }

        var aufschluesse = await db.Aufschluesse
            .AsNoTracking()
            .Where(a => a.ProjectId == id)
            .OrderBy(a => a.Id)
            .ToListAsync();

        var features = aufschluesse.Select(a =>
        {
            var (lat, lon) = CoordinateService.ToWgs84(a.Rechtswert, a.Hochwert);
            return new
            {
                type = "Feature",
                geometry = new { type = "Point", coordinates = new[] { Math.Round(lon, 6), Math.Round(lat, 6) } },
                properties = new
                {
                    id = a.Id,
                    bezeichnung = a.Bezeichnung,
                    art = a.Art.ToString(),
                    endteufeM = a.EndteufeM,
                    grundwasserM = a.GrundwasserM,
                    ansatzhoeheMNhn = a.AnsatzhoeheMNhn,
                    rechtswert = a.Rechtswert,
                    hochwert = a.Hochwert,
                },
            };
        }).ToList();

        return new JsonResult(new { type = "FeatureCollection", features });
    }

    /// <summary>Short line for the side panel, e.g. "3,5 m · Grundwasser 2,8 m" (English: "3,5 m · groundwater 2,8 m").</summary>
    public string Kurzinfo(ListeneintragZeile z) =>
        z.EndteufeM.ToString("0.0", De) + " m"
        + (z.GrundwasserM is double gw ? " · " + loc["Grundwasser"].Value + " " + gw.ToString("0.0", De) + " m" : string.Empty);
}
