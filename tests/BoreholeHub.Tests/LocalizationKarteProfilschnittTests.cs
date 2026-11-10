using System.Net;
using BoreholeHub.Core.Entities;
using BoreholeHub.Web.Data;
using Microsoft.EntityFrameworkCore;

namespace BoreholeHub.Tests;

/// <summary>English map and cross-section pages, including their script texts and SVG labels (docs/i18n.md).</summary>
public class LocalizationKarteProfilschnittTests : IClassFixture<WebAppFactory>
{
    private readonly WebAppFactory _factory;

    public LocalizationKarteProfilschnittTests(WebAppFactory factory) => _factory = factory;

    /// <summary>Seeds the synthetic demo project and returns its id.</summary>
    private async Task<int> SeedDemoAsync()
    {
        int id = 0;
        await _factory.WithDbAsync(async db => id = await DemoDataSeeder.SeedAsync(db));
        return id;
    }

    /// <summary>Ids of the Kleinrammbohrungen of a project in list order.</summary>
    private async Task<List<int>> KrbIdsAsync(int projectId)
    {
        List<int> ids = [];
        await _factory.WithDbAsync(async db =>
            ids = await db.Aufschluesse.AsNoTracking()
                .Where(a => a.ProjectId == projectId && a.Art == AufschlussArt.Kleinrammbohrung)
                .OrderBy(a => a.Id)
                .Select(a => a.Id)
                .ToListAsync());
        return ids;
    }

    /// <summary>Adds a project with two Kleinrammbohrungen, one with groundwater, and returns its id.</summary>
    private async Task<int> SeedKarteProjektAsync()
    {
        int id = 0;
        await _factory.WithDbAsync(async db =>
        {
            var projekt = new Project
            {
                Name = "Sprachtest Karte",
                Ort = "Schwalmstadt",
                Auftraggeber = "Muster GmbH (fiktiv)",
                Beschreibung = "Synthetisches Testprojekt.",
                ErstelltAm = new DateTime(2026, 11, 2),
            };
            projekt.Aufschluesse.Add(new Aufschluss { Bezeichnung = "KRB 1", Art = AufschlussArt.Kleinrammbohrung, Rechtswert = 512860, Hochwert = 5638730, AnsatzhoeheMNhn = 215.1, EndteufeM = 3.5, GrundwasserM = 2.8, Datum = new DateOnly(2026, 10, 28) });
            projekt.Aufschluesse.Add(new Aufschluss { Bezeichnung = "KRB 2", Art = AufschlussArt.Kleinrammbohrung, Rechtswert = 512880, Hochwert = 5638760, AnsatzhoeheMNhn = 215.4, EndteufeM = 4.0, Datum = new DateOnly(2026, 10, 28) });
            db.Projects.Add(projekt);
            await db.SaveChangesAsync();
            id = projekt.Id;
        });
        return id;
    }

    [Fact]
    public async Task Karte_InEnglish_ShowsEnglishPanelLegendAndScriptTexts()
    {
        int id = await SeedKarteProjektAsync();
        var client = await LanguageTestHelper.CreateEnglishClientAsync(_factory);

        var html = await client.GetStringAsync($"/Projekte/{id}/Karte");

        Assert.Contains("<html lang=\"en\"", html);
        Assert.Contains("<h2 class=\"karte-title\">Layers</h2>", html);
        Assert.Contains("<span>Geology (GK 25, HLNUG)</span>", html);
        Assert.Contains("aria-label=\"Opacity, geology (GK 25)\"", html);
        Assert.Contains("<a class=\"btn btn-secondary btn-sm\" href=\"/Projekte/" + id + "/Profilschnitt\">Cross-section</a>", html);
        Assert.Contains("aria-label=\"Map panel\"", html);
        Assert.Contains("aria-label=\"Map of exploration points\"", html);
        Assert.Contains("aria-label=\"Legend\"", html);
        Assert.Contains("Small percussion borehole (KRB)", html);
        Assert.Contains("Dynamic probing (DPH)", html);
        Assert.Contains("Groundwater encountered", html);
        Assert.Contains("data-t-profile=\"Open profile\"", html);
        Assert.Contains("data-t-krb=\"Small percussion borehole\"", html);
        Assert.Contains("data-t-below=\"m below ground level\"", html);
        Assert.Contains("data-t-load-error=\"The points could not be loaded.\"", html);
        Assert.Contains("3,5 m · groundwater 2,8 m", WebUtility.HtmlDecode(html));
        Assert.DoesNotContain("Ebenen", html);
        Assert.DoesNotContain("Legende", html);
        Assert.DoesNotContain("Karte der Aufschlüsse", html);
        Assert.DoesNotContain("Profil öffnen", html);
    }

    [Fact]
    public async Task Karte_NoCookie_StaysGerman_WithGermanScriptTextsInData()
    {
        int id = await SeedKarteProjektAsync();

        var html = await _factory.CreateClient().GetStringAsync($"/Projekte/{id}/Karte");

        Assert.Contains("<html lang=\"de\"", html);
        Assert.Contains("<h2 class=\"karte-title\">Ebenen</h2>", html);
        Assert.Contains("<span>Geologie (GK 25, HLNUG)</span>", html);
        Assert.Contains("aria-label=\"Legende\"", html);
        Assert.Contains("data-t-profile=\"Profil öffnen\"", html);
        Assert.Contains("data-t-below=\"m u. GOK\"", html);
        Assert.Contains("3,5 m · Grundwasser 2,8 m", WebUtility.HtmlDecode(html));
        Assert.DoesNotContain("Layers", html);
    }

    [Fact]
    public async Task Profilschnitt_InEnglish_ShowsEnglishPageAndSvgLabels()
    {
        int id = await SeedDemoAsync();
        var client = await LanguageTestHelper.CreateEnglishClientAsync(_factory);

        var html = await client.GetStringAsync($"/Projekte/{id}/Profilschnitt");

        Assert.Contains("<html lang=\"en\"", html);
        Assert.Contains("<h1>Cross-section</h1>", html);
        Assert.Contains("Select boreholes", html);
        Assert.Contains(">Draw section</button>", html);
        Assert.Contains("<title id=\"ps-title\">Cross-section KRB", html);
        Assert.Contains("Interpreted boundary", html);
        Assert.Contains("Groundwater (m NHN)", html);
        Assert.Contains("Distance along the section [m]", html);
        Assert.Contains("Schematic interpretation between the exploration points", html);
        Assert.Contains("<title id=\"pl-title\">Site plan of the exploration points with section line</title>", html);
        Assert.Contains("● KRB (no.)   ◆ DPH   — section line", html);
        Assert.DoesNotContain("Bohrungen wählen", html);
        Assert.DoesNotContain("Interpretierte Grenze", html);
        Assert.DoesNotContain("Abstand entlang des Schnitts", html);
        Assert.DoesNotContain("Schnitt zeichnen", html);
    }

    [Fact]
    public async Task Profilschnitt_InEnglish_ShowsEnglishMessageForUnusableSelection()
    {
        int id = await SeedDemoAsync();
        var client = await LanguageTestHelper.CreateEnglishClientAsync(_factory);

        var html = await client.GetStringAsync($"/Projekte/{id}/Profilschnitt?punkte=1");

        Assert.Contains("<p class=\"ps-alert\" role=\"alert\">Please choose 2 to 4 small percussion boreholes. The default section is shown.</p>", html);
    }

    [Fact]
    public async Task Profilschnitt_NoCookie_StaysGermanIncludingSvgLabels()
    {
        int id = await SeedDemoAsync();

        var html = await _factory.CreateClient().GetStringAsync($"/Projekte/{id}/Profilschnitt");

        Assert.Contains("<html lang=\"de\"", html);
        Assert.Contains("<h1>Profilschnitt</h1>", html);
        Assert.Contains("<h2 class=\"ps-select-title\" id=\"ps-select-title\">Bohrungen wählen</h2>", html);
        Assert.Contains("<title id=\"ps-title\">Profilschnitt KRB", html);
        Assert.Contains("<title id=\"pl-title\">Lageplan der Aufschlüsse mit Schnittlinie</title>", html);
        Assert.Contains("Interpretierte Grenze", html);
        Assert.Contains("Abstand entlang des Schnitts [m]", html);
        Assert.DoesNotContain("Cross-section", html);
    }

    [Fact]
    public async Task Profilschnitt_InEnglish_WithChosenPoints_DrawsEnglishDescription()
    {
        int id = await SeedDemoAsync();
        var krb = await KrbIdsAsync(id);
        var client = await LanguageTestHelper.CreateEnglishClientAsync(_factory);

        var html = await client.GetStringAsync($"/Projekte/{id}/Profilschnitt?punkte={krb[0]},{krb[1]}");

        Assert.Contains("<desc id=\"ps-desc\">Geological section over ", html);
        Assert.Contains("vertical exaggeration 1:", html);
        Assert.Contains("Schematic interpretation between the exploration points", html);
        Assert.DoesNotContain("Geologischer Schnitt", html);
    }
}
