using BoreholeHub.Web.Data;
using Microsoft.AspNetCore.Mvc.RazorPages;
using Microsoft.EntityFrameworkCore;

namespace BoreholeHub.Web.Pages;

public class IndexModel(BoreholeDbContext db) : PageModel
{
    /// <summary>Number of projects in the database.</summary>
    public int ProjektAnzahl { get; private set; }

    /// <summary>Number of Aufschlüsse (all projects) in the database.</summary>
    public int AufschlussAnzahl { get; private set; }

    public async Task OnGetAsync()
    {
        ProjektAnzahl = await db.Projects.CountAsync();
        AufschlussAnzahl = await db.Aufschluesse.CountAsync();
    }
}
