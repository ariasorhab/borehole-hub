using System.Globalization;
using BoreholeHub.Core.Entities;
using BoreholeHub.Core.Services;
using BoreholeHub.Web.Data;
using BoreholeHub.Web.Drawing;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Localization;

namespace BoreholeHub.Web.Pages.Aufschluesse;

public class DetailsModel(BoreholeDbContext db, IStringLocalizer<DetailsModel> loc) : PageModel
{
    private static readonly CultureInfo De = CultureInfo.GetCultureInfo("de-DE");

    /// <summary>Localizer of this page model (keys in Resources/Pages/Aufschluesse/DetailsModel.en.resx); used by the view for the helper texts.</summary>
    public IStringLocalizer<DetailsModel> Loc => loc;

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
            BohrprofilSvgHtml = BohrprofilSvg.Render(Bohrung, Schichten, ScaleMaxM, loc);
        }

        if (Sonde is not null)
        {
            RammdiagrammSvgHtml = RammdiagrammSvg.Render(Sonde, Sonde.Messwerte.OrderBy(m => m.TiefeBisM).ToList(), ScaleMaxM, loc);
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

    /// <summary>Distance text, e.g. "2,2 m entfernt"; pass a localizer for English (null = German).</summary>
    public static string AbstandText(double m, IStringLocalizer? loc = null) => Txt(loc, "{0} m entfernt", m.ToString("0.0", De));

    /// <summary>Frost class text; pass a localizer for English (null = German). Other values are shown as they are.</summary>
    public static string FrostText(string? klasse, IStringLocalizer? loc = null) => klasse switch
    {
        "F1" => Txt(loc, "F1 nicht frostempfindlich"),
        "F2" => Txt(loc, "F2 gering bis mittel frostempfindlich"),
        "F3" => Txt(loc, "F3 sehr frostempfindlich"),
        null or "" => "–",
        _ => klasse,
    };

    /// <summary>German text when no localizer is passed; otherwise the localized value (docs/i18n.md, rule (b)/(c)).</summary>
    private static string Txt(IStringLocalizer? loc, string key, params object[] args) =>
        loc is null ? string.Format(key, args) : loc[key, args].Value;

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

    /// <summary>Infiltration badge (CSS class and German text), as used by the lab page.</summary>
    public static (string Css, string Text) Versickerung(double? kf) => Versickerung(kf, null);

    /// <summary>Infiltration badge (CSS class and text) with the page localizer; null gives the German text.</summary>
    public static (string Css, string Text) Versickerung(double? kf, IStringLocalizer? loc) => InfiltrationService.Assess(kf) switch
    {
        InfiltrationResult.Geeignet => ("badge-ok", Txt(loc, "Geeignet")),
        InfiltrationResult.ZuGeringDurchlaessig => ("badge-warn", Txt(loc, "Zu gering durchlässig")),
        InfiltrationResult.ZuDurchlaessig => ("badge-warn", Txt(loc, "Zu durchlässig")),
        _ => ("badge-muted", Txt(loc, "Unbekannt")),
    };
}
