using BoreholeHub.Web.Data;
using Microsoft.AspNetCore.Mvc.RazorPages;
using Microsoft.EntityFrameworkCore;

namespace BoreholeHub.Web.Pages;

public class IndexModel(BoreholeDbContext db) : PageModel
{
    /// <summary>Number of projects in the database.</summary>
    public int ProjektAnzahl { get; private set; }

    public async Task OnGetAsync()
    {
        ProjektAnzahl = await db.Projects.CountAsync();
    }
}
