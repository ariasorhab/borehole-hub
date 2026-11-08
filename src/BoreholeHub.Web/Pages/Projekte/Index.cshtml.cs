using BoreholeHub.Core.Entities;
using BoreholeHub.Web.Data;
using Microsoft.AspNetCore.Mvc.RazorPages;
using Microsoft.EntityFrameworkCore;

namespace BoreholeHub.Web.Pages.Projekte;

public class IndexModel(BoreholeDbContext db) : PageModel
{
    /// <summary>One row of the project list.</summary>
    public record ProjektZeile(int Id, string Name, string Ort, string Auftraggeber, DateTime ErstelltAm, int Kleinrammbohrungen, int Rammsondierungen);

    public IReadOnlyList<ProjektZeile> Projekte { get; private set; } = [];

    public async Task OnGetAsync()
    {
        // Counts are computed in the database; newest project first.
        Projekte = await db.Projects
            .AsNoTracking()
            .OrderByDescending(p => p.ErstelltAm)
            .ThenBy(p => p.Name)
            .Select(p => new ProjektZeile(
                p.Id,
                p.Name,
                p.Ort,
                p.Auftraggeber,
                p.ErstelltAm,
                p.Aufschluesse.Count(a => a.Art == AufschlussArt.Kleinrammbohrung),
                p.Aufschluesse.Count(a => a.Art == AufschlussArt.Rammsondierung)))
            .ToListAsync();
    }
}
