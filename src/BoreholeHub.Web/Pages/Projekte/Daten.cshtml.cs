using System.Text;
using System.Text.Json;
using BoreholeHub.Core.Entities;
using BoreholeHub.Core.Services;
using BoreholeHub.Web.Data;
using BoreholeHub.Web.Import;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using Microsoft.EntityFrameworkCore;

namespace BoreholeHub.Web.Pages.Projekte;

// Hard cap for the whole request; each file is limited to 1 MB in the handler with a friendly message.
[RequestSizeLimit(3 * 1024 * 1024)]
public class DatenModel(BoreholeDbContext db) : PageModel
{
    public const long MaxDateiBytes = 1024 * 1024;

    public Project Projekt { get; private set; } = null!;

    /// <summary>Set after an import attempt.</summary>
    public bool ImportVersucht { get; private set; }

    public IReadOnlyList<string> Fehler { get; private set; } = [];

    public int ImportierteAufschluesse { get; private set; }

    public int ImportierteSchichten { get; private set; }

    public bool Erfolg => ImportVersucht && Fehler.Count == 0;

    public async Task<IActionResult> OnGetAsync(int id)
    {
        return await LoadProjektAsync(id) ? Page() : NotFound();
    }

    // ---------- templates ----------

    public IActionResult OnGetVorlageAufschluesse(int id) => Csv("vorlage-aufschluesse.csv",
        CsvImportService.AufschluesseHeader,
        "KRB 6;KRB;516296,5;5640626;212,8;3,0;27.10.2026;",
        "DPH 4;DPH;516300;5640628;212,7;4,0;28.10.2026;2,9");

    public IActionResult OnGetVorlageSchichten(int id) => Csv("vorlage-schichten.csv",
        CsvImportService.SchichtenHeader,
        "KRB 6;0,00;0,30;U, h, s';Mutterboden: Schluff, humos, schwach sandig;OH;dunkelbraun;weich;B1;F2;1E-06",
        "KRB 6;0,30;3,00;U, t, s';Auenlehm: Schluff, tonig, schwach sandig;TL;braun;steif;B2;F3;5,0E-08");

    private static FileContentResult Csv(string fileName, params string[] lines)
    {
        var bytes = new UTF8Encoding(true).GetPreamble().Concat(Encoding.UTF8.GetBytes(string.Join("\r\n", lines) + "\r\n")).ToArray();
        return new FileContentResult(bytes, "text/csv; charset=utf-8") { FileDownloadName = fileName };
    }

    // ---------- GeoJSON export ----------

    public async Task<IActionResult> OnGetGeoJsonAsync(int id)
    {
        var projekt = await db.Projects.AsNoTracking().FirstOrDefaultAsync(p => p.Id == id);
        if (projekt is null)
        {
            return NotFound();
        }

        var aufschluesse = await db.Aufschluesse.AsNoTracking()
            .Where(a => a.ProjectId == id)
            .Include(a => a.Schichten)
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
                    projekt = projekt.Name,
                    datum = a.Datum.ToString("yyyy-MM-dd"),
                    schichten = a.Schichten.Count,
                },
            };
        }).ToList();

        var json = JsonSerializer.SerializeToUtf8Bytes(new { type = "FeatureCollection", features }, new JsonSerializerOptions { WriteIndented = true });
        return new FileContentResult(json, "application/geo+json") { FileDownloadName = $"{Slug(projekt.Name)}-aufschluesse.geojson" };
    }

    /// <summary>File-name friendly form of a project name: lower case, umlauts transliterated, other characters become "-".</summary>
    public static string Slug(string name)
    {
        var sb = new StringBuilder();
        foreach (char c in name.ToLowerInvariant())
        {
            switch (c)
            {
                case 'ä': sb.Append("ae"); break;
                case 'ö': sb.Append("oe"); break;
                case 'ü': sb.Append("ue"); break;
                case 'ß': sb.Append("ss"); break;
                case >= 'a' and <= 'z': case >= '0' and <= '9': sb.Append(c); break;
                default: if (sb.Length > 0 && sb[^1] != '-') { sb.Append('-'); } break;
            }
        }

        string slug = sb.ToString().Trim('-');
        return slug.Length == 0 ? "projekt" : slug;
    }

    // ---------- import ----------

    public async Task<IActionResult> OnPostImportAsync(int id, IFormFile? aufschluesseCsv, IFormFile? schichtenCsv)
    {
        if (!await LoadProjektAsync(id))
        {
            return NotFound();
        }

        ImportVersucht = true;
        var fehler = new List<string>();
        string? a = await ReadAsync(aufschluesseCsv, "Aufschlüsse-Datei", fehler);
        string? s = await ReadAsync(schichtenCsv, "Schichten-Datei", fehler);
        if (fehler.Count > 0)
        {
            Fehler = fehler;
            return Page();
        }

        var existing = await db.Aufschluesse.AsNoTracking().Where(x => x.ProjectId == id).Include(x => x.Schichten).ToListAsync();
        var result = new CsvImportService().Parse(a, s, existing);
        if (result.Fehler.Count > 0)
        {
            Fehler = result.Fehler;
            return Page();
        }

        // One SaveChanges = one transaction: all or nothing.
        foreach (var neu in result.NeueAufschluesse)
        {
            neu.ProjectId = id;
            db.Aufschluesse.Add(neu);
        }

        db.Schichten.AddRange(result.NeueSchichten);
        await db.SaveChangesAsync();
        ImportierteAufschluesse = result.NeueAufschluesse.Count;
        ImportierteSchichten = result.NeueSchichten.Count;
        Fehler = [];
        return Page();
    }

    private static async Task<string?> ReadAsync(IFormFile? file, string label, List<string> fehler)
    {
        if (file is null || file.Length == 0) { return null; }
        if (file.Length > MaxDateiBytes)
        {
            fehler.Add($"{label}: Die Datei ist größer als 1 MB.");
            return null;
        }

        using var ms = new MemoryStream();
        await file.CopyToAsync(ms);
        return CsvImportService.Decode(ms.ToArray());
    }

    private async Task<bool> LoadProjektAsync(int id)
    {
        var projekt = await db.Projects.AsNoTracking().FirstOrDefaultAsync(p => p.Id == id);
        if (projekt is null) { return false; }
        Projekt = projekt;
        return true;
    }

    public static string Plural(int n, string einzahl, string mehrzahl) => n == 1 ? $"{n} {einzahl}" : $"{n} {mehrzahl}";
}
