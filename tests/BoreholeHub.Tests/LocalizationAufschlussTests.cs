using System.Net;
using BoreholeHub.Web.Data;
using Microsoft.EntityFrameworkCore;

namespace BoreholeHub.Tests;

/// <summary>English borehole log and probing diagram page, including the SVG labels (docs/i18n.md).</summary>
public class LocalizationAufschlussTests : IClassFixture<WebAppFactory>
{
    private readonly WebAppFactory _factory;

    public LocalizationAufschlussTests(WebAppFactory factory) => _factory = factory;

    /// <summary>Seeds the demo project and returns the ids of its Aufschluesse by designation.</summary>
    private async Task<Dictionary<string, int>> SeedDemoAsync()
    {
        var ids = new Dictionary<string, int>();
        await _factory.WithDbAsync(async db =>
        {
            int pid = await DemoDataSeeder.SeedAsync(db);
            foreach (var a in await db.Aufschluesse.Where(x => x.ProjectId == pid).ToListAsync())
            {
                ids[a.Bezeichnung] = a.Id;
            }
        });
        return ids;
    }

    private static string Decode(string html) => WebUtility.HtmlDecode(html);

    [Fact]
    public async Task Krb_InEnglish_ShowsEnglishLogAndNotices()
    {
        var ids = await SeedDemoAsync();
        var client = await LanguageTestHelper.CreateEnglishClientAsync(_factory);

        var response = await client.GetAsync($"/Aufschluesse/{ids["KRB 3"]}");
        var html = Decode(await response.Content.ReadAsStringAsync());

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Contains("<html lang=\"en\"", html);
        Assert.Contains("Small percussion borehole · Final depth ", html);
        Assert.Contains("Ground level ", html);
        Assert.Contains("Groundwater 2,90 m below ground level", html);
        Assert.Contains("aria-current=\"page\">Overview</a>", html);
        Assert.Contains("<h2 class=\"section-title\" id=\"schichten-titel\">Layer description log KRB 3</h2>", html);
        Assert.Contains("<th scope=\"col\" class=\"num\">From–To [m]</th>", html);
        Assert.Contains("<th scope=\"col\">Infiltration</th>", html);
        Assert.Contains("Simplified pre-check: the kf value does not replace a design to DWA-A 138-1.", html);
        Assert.Contains("Drawing based on DIN 4023, simplified", html);
        Assert.Contains("(nearest dynamic probing)", html);
        Assert.Contains("<h2 class=\"section-title\" id=\"proben-titel\">Samples</h2>", html);
        Assert.Contains(">Lab values</a>", html);
        Assert.DoesNotContain("Schichtenverzeichnis", html);
        Assert.DoesNotContain("Vereinfachte Vorprüfung", html);
        Assert.DoesNotContain("Zurück zur Karte", html);
    }

    [Fact]
    public async Task Dph_InEnglish_ShowsEnglishProbingDiagramAndNearestBorehole()
    {
        var ids = await SeedDemoAsync();
        var client = await LanguageTestHelper.CreateEnglishClientAsync(_factory);

        var html = Decode(await client.GetStringAsync($"/Aufschluesse/{ids["DPH 2"]}"));

        Assert.Contains("<html lang=\"en\"", html);
        Assert.Contains("Dynamic probing · Final depth", html);
        Assert.Contains("KRB 3 · 2,2 m away", html);
        Assert.Contains("(nearest small percussion borehole)", html);
        Assert.Contains("<title id=\"rd-title\">Probing diagram DPH 2</title>", html);
        Assert.Contains("DPH 2 · heavy dynamic probe (DIN EN ISO 22476-2)", html);
        Assert.Contains("N10 (blows per 10 cm)", html);
        Assert.Contains("N10 = blow count per 10 cm of penetration", html);
        Assert.DoesNotContain("Endteufe", html);
        Assert.DoesNotContain("Schichtenverzeichnis", html);
        Assert.DoesNotContain("Rammdiagramm", html);
        Assert.DoesNotContain("schwere Rammsonde", html);
        Assert.DoesNotContain("entfernt", html);
    }

    [Fact]
    public async Task SvgLabels_InEnglish_ForBoreholeLog()
    {
        var ids = await SeedDemoAsync();
        var client = await LanguageTestHelper.CreateEnglishClientAsync(_factory);

        var html = Decode(await client.GetStringAsync($"/Aufschluesse/{ids["KRB 3"]}"));

        Assert.Contains("<title id=\"bp-title\">Borehole log KRB 3</title>", html);
        Assert.Contains("Layer profile of small percussion borehole KRB 3, final depth ", html);
        Assert.Contains("Groundwater at 2,90 m below ground level.", html);
        Assert.Contains("<text class=\"dr-colhead\" x=\"4\"", html);
        Assert.Contains(">Depth [m]</text>", html);
        Assert.Contains("E 516290 / N 5640622 (EPSG:25832)", html);
        Assert.DoesNotContain("Bohrprofil KRB", html);
        Assert.DoesNotContain("Schichtenprofil der", html);
    }

    [Fact]
    public async Task Krb_NoCookie_StaysGerman()
    {
        var ids = await SeedDemoAsync();

        var html = Decode(await _factory.CreateClient().GetStringAsync($"/Aufschluesse/{ids["KRB 3"]}"));

        Assert.Contains("<html lang=\"de\"", html);
        Assert.Contains("Kleinrammbohrung · Endteufe", html);
        Assert.Contains("Grundwasser 2,90 m u. GOK", html);
        Assert.Contains("<h2 class=\"section-title\" id=\"schichten-titel\">Schichtenverzeichnis KRB 3</h2>", html);
        Assert.Contains("<th scope=\"col\" class=\"num\">Von–Bis [m]</th>", html);
        Assert.Contains("Vereinfachte Vorprüfung: Der kf-Wert ersetzt keine Bemessung nach DWA-A 138-1.", html);
        Assert.Contains("<title id=\"bp-title\">Bohrprofil KRB 3</title>", html);
        Assert.Contains("Schichtenprofil der Kleinrammbohrung KRB 3, Endteufe", html);
        Assert.DoesNotContain("Layer description log", html);
    }
}
