using System.Globalization;
using BoreholeHub.Core.Entities;
using BoreholeHub.Core.Services;
using BoreholeHub.Web.Data;
using BoreholeHub.Web.Drawing;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using Microsoft.EntityFrameworkCore;

namespace BoreholeHub.Web.Pages.Aufschluesse;

public class DetailsModel(BoreholeDbContext db) : PageModel
{
    private static readonly CultureInfo De = CultureInfo.GetCultureInfo("de-DE");

    public Aufschluss Aufschluss { get; private set; } = null!;

    public Project Projekt { get; private set; } = null!;

    /// <summary>The Kleinrammbohrung whose log is drawn (the Aufschluss itself, or the nearest one for a Rammsondierung).</summary>
    public Aufschluss? Bohrung { get; private set; }

    /// <summary>The Rammsondierung whose chart is drawn (the Aufschluss itself, or the nearest one for a Kleinrammbohrung).</summary>
    public Aufschluss? Sonde { get; private set; }

    /// <summary>Distance in metres between Bohrung and Sonde (EPSG:25832), if both exist.</summary>
    public double? AbstandM { get; private set; }

    public IReadOnlyList<Schicht> Schichten { get; private set; } = [];

    public IReadOnlyList<Probe> Proben { get; private set; } = [];

    public string? BohrprofilSvgHtml { get; private set; }

    public string? RammdiagrammSvgHtml { get; private set; }

    public double ScaleMaxM { get; private set; }

    public bool HatKf => Schichten.Any(s => s.KfWertMs is not null);

    public bool IstKleinrammbohrung => Aufschluss.Art == AufschlussArt.Kleinrammbohrung;

    public async Task<IActionResult> OnGetAsync(int id)
    {
        var a = await db.Aufschluesse.AsNoTracking()
            .Include(x => x.Project)
            .Include(x => x.Schichten)
            .Include(x => x.Proben)
            .Include(x => x.Messwerte)
            .AsSplitQuery()
            .FirstOrDefaultAsync(x => x.Id == id);
        if (a is null)
        {
            return NotFound();
        }

        Aufschluss = a;
        Projekt = a.Project;

        var others = await db.Aufschluesse.AsNoTracking()
            .Where(x => x.ProjectId == a.ProjectId && x.Art != a.Art)
            .ToListAsync();
        var nearest = others
            .OrderBy(x => Distance(a, x))
            .FirstOrDefault();

        if (a.Art == AufschlussArt.Kleinrammbohrung)
        {
            Bohrung = a;
            if (nearest is not null)
            {
                Sonde = await LoadAsync(nearest.Id, withLayers: false);
            }
        }
        else
        {
            Sonde = a;
            if (nearest is not null)
            {
                Bohrung = await LoadAsync(nearest.Id, withLayers: true);
            }
        }

        if (Bohrung is not null && Sonde is not null)
        {
            AbstandM = Distance(Bohrung, Sonde);
        }

        Schichten = Bohrung?.Schichten.OrderBy(s => s.VonM).ToList() ?? [];
        Proben = Bohrung?.Proben.OrderBy(p => p.VonM).ToList() ?? [];

        // One depth scale for both drawings: the deeper of the two, rounded up to 0.5 m.
        var depths = new List<double>();
        if (Bohrung is not null) { depths.Add(Bohrung.EndteufeM); }
        if (Sonde is not null) { depths.Add(Sonde.EndteufeM); }
        ScaleMaxM = Math.Ceiling(Math.Max(0.5, depths.DefaultIfEmpty(0.5).Max()) * 2) / 2;

        if (Bohrung is not null)
        {
            BohrprofilSvgHtml = BohrprofilSvg.Render(Bohrung, Schichten, ScaleMaxM);
        }

        if (Sonde is not null)
        {
            RammdiagrammSvgHtml = RammdiagrammSvg.Render(Sonde, Sonde.Messwerte.OrderBy(m => m.TiefeBisM).ToList(), ScaleMaxM);
        }

        return Page();
    }

    private async Task<Aufschluss?> LoadAsync(int id, bool withLayers)
    {
        IQueryable<Aufschluss> query = db.Aufschluesse.AsNoTracking();
        query = withLayers
            ? query.Include(x => x.Schichten).Include(x => x.Proben)
            : query.Include(x => x.Messwerte);
        return await query.AsSplitQuery().FirstOrDefaultAsync(x => x.Id == id);
    }

    public static double Distance(Aufschluss a, Aufschluss b) =>
        Math.Sqrt(Math.Pow(a.Rechtswert - b.Rechtswert, 2) + Math.Pow(a.Hochwert - b.Hochwert, 2));

    // ---------- formatting helpers used by the view ----------

    public static string Meter(double v) => v.ToString("0.00", De);

    public static string AbstandText(double m) => m.ToString("0.0", De) + " m entfernt";

    public static string FrostText(string? klasse) => klasse switch
    {
        "F1" => "F1 nicht frostempfindlich",
        "F2" => "F2 gering bis mittel frostempfindlich",
        "F3" => "F3 sehr frostempfindlich",
        null or "" => "–",
        _ => klasse,
    };

    private const string Superscripts = "⁰¹²³⁴⁵⁶⁷⁸⁹";

    /// <summary>kf in the form "5,0 · 10⁻⁸".</summary>
    public static string KfText(double? kf)
    {
        if (kf is null or <= 0) { return "–"; }
        int exp = (int)Math.Floor(Math.Log10(kf.Value));
        double mant = Math.Round(kf.Value / Math.Pow(10, exp), 1);
        if (mant >= 10) { mant /= 10; exp++; }
        string e = (exp < 0 ? "⁻" : string.Empty) + string.Concat(Math.Abs(exp).ToString(CultureInfo.InvariantCulture).Select(c => Superscripts[c - '0']));
        return mant.ToString("0.0", De) + " · 10" + e;
    }

    public static (string Css, string Text) Versickerung(double? kf) => InfiltrationService.Assess(kf) switch
    {
        InfiltrationResult.Geeignet => ("badge-ok", "Geeignet"),
        InfiltrationResult.ZuGeringDurchlaessig => ("badge-warn", "Zu gering durchlässig"),
        InfiltrationResult.ZuDurchlaessig => ("badge-warn", "Zu durchlässig"),
        _ => ("badge-muted", "Unbekannt"),
    };
}
