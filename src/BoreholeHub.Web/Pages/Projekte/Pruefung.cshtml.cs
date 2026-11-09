using BoreholeHub.Core.Entities;
using BoreholeHub.Core.Services;
using BoreholeHub.Web.Data;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using Microsoft.EntityFrameworkCore;

namespace BoreholeHub.Web.Pages.Projekte;

public class PruefungModel(BoreholeDbContext db) : PageModel
{
    /// <summary>One table row: a finding, or the green row of an Aufschluss without findings.</summary>
    public record Zeile(string Stufe, string StufeCss, string RowCss, string Aufschluss, int AufschlussId, string Regel, string Meldung);

    public Project Projekt { get; private set; } = null!;

    public IReadOnlyList<Zeile> Zeilen { get; private set; } = [];

    public int AnzahlAufschluesse { get; private set; }

    public int AnzahlFehler { get; private set; }

    public int AnzahlHinweise { get; private set; }

    /// <summary>Aufschlüsse without any finding.</summary>
    public int OhneBefund { get; private set; }

    public async Task<IActionResult> OnGetAsync(int id)
    {
        var projekt = await db.Projects.AsNoTracking().FirstOrDefaultAsync(p => p.Id == id);
        if (projekt is null)
        {
            return NotFound();
        }

        Projekt = projekt;
        var aufschluesse = await LadeAufschluesseAsync(db, id);
        var befunde = DataQualityService.Pruefen(aufschluesse);

        AnzahlAufschluesse = aufschluesse.Count;
        AnzahlFehler = befunde.Count(b => b.Stufe == PruefStufe.Fehler);
        AnzahlHinweise = befunde.Count(b => b.Stufe == PruefStufe.Hinweis);

        var mitBefund = befunde.Select(b => b.AufschlussId).ToHashSet();
        var ohne = aufschluesse.Where(a => !mitBefund.Contains(a.Id)).ToList();
        OhneBefund = ohne.Count;

        var zeilen = new List<Zeile>();
        foreach (var b in befunde)
        {
            bool fehler = b.Stufe == PruefStufe.Fehler;
            zeilen.Add(new Zeile(
                fehler ? "Fehler" : "Hinweis",
                fehler ? "badge-bad" : "badge-warn",
                fehler ? "row-bad" : "row-warn",
                b.Aufschluss,
                b.AufschlussId,
                b.Regel,
                b.Meldung));
        }

        foreach (var a in ohne.OrderBy(a => a.Bezeichnung, StringComparer.Ordinal).ThenBy(a => a.Id))
        {
            zeilen.Add(new Zeile("OK", "badge-ok", "row-ok", a.Bezeichnung, a.Id, "–", "Alle Prüfungen bestanden."));
        }

        Zeilen = zeilen;
        return Page();
    }

    /// <summary>All Aufschlüsse of a project with the layers, samples and blow counts that the checks need.</summary>
    public static Task<List<Aufschluss>> LadeAufschluesseAsync(BoreholeDbContext db, int projektId) =>
        db.Aufschluesse.AsNoTracking()
            .Where(a => a.ProjectId == projektId)
            .Include(a => a.Schichten)
            .Include(a => a.Proben)
            .Include(a => a.Messwerte)
            .AsSplitQuery()
            .OrderBy(a => a.Id)
            .ToListAsync();
}
