using BoreholeHub.Web.Data;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;

namespace BoreholeHub.Web.Pages;

/// <summary>Loads the synthetic demo project (POST) and opens its map. Antiforgery validation stays on.</summary>
public class DemoModel(BoreholeDbContext db) : PageModel
{
    /// <summary>A direct visit to /Demo has nothing to show.</summary>
    public IActionResult OnGet() => Redirect("/");

    public async Task<IActionResult> OnPostAsync(CancellationToken ct)
    {
        int id = await DemoDataSeeder.SeedAsync(db, ct);
        return Redirect($"/Projekte/{id}/Karte");
    }
}
