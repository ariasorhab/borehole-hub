using System.Net;
using BoreholeHub.Core.Entities;

namespace BoreholeHub.Tests;

/// <summary>English rendering of the start, project list, project overview (with tab bar) and error pages (docs/i18n.md).</summary>
public class LocalizationSmallPagesTests : IClassFixture<WebAppFactory>
{
    private readonly WebAppFactory _factory;

    public LocalizationSmallPagesTests(WebAppFactory factory) => _factory = factory;

    /// <summary>Adds one project with a small percussion borehole and returns its id.</summary>
    private async Task<int> SeedProjektAsync(string name)
    {
        int id = 0;
        await _factory.WithDbAsync(async db =>
        {
            var projekt = new Project
            {
                Name = name,
                Ort = "Schwalmstadt",
                Auftraggeber = "Muster GmbH (fiktiv)",
                Beschreibung = "Synthetisches Testprojekt.",
                ErstelltAm = new DateTime(2026, 11, 2),
            };
            projekt.Aufschluesse.Add(new Aufschluss
            {
                Bezeichnung = "KRB 1",
                Art = AufschlussArt.Kleinrammbohrung,
                Rechtswert = 512860,
                Hochwert = 5638730,
                AnsatzhoeheMNhn = 215.1,
                EndteufeM = 4.0,
                GrundwasserM = 2.5,
                Datum = new DateOnly(2026, 10, 28),
            });
            db.Projects.Add(projekt);
            await db.SaveChangesAsync();
            id = projekt.Id;
        });
        return id;
    }

    [Fact]
    public async Task Start_InEnglish_ShowsEnglishTextAndNoGermanButtons()
    {
        var client = await LanguageTestHelper.CreateEnglishClientAsync(_factory);

        var html = await client.GetStringAsync("/");

        Assert.Contains("<html lang=\"en\"", html);
        Assert.Contains("Record exploration points, view them on the map, check layers and lab values.", html);
        Assert.Contains(">Load demo project</button>", html);
        Assert.Contains(">View projects</a>", html);
        Assert.Contains("aria-label=\"Workflow\"", html);
        Assert.Contains("<figcaption>Schematic illustration, not measured data.</figcaption>", html);
        Assert.DoesNotContain("Demo-Projekt laden", html);
        Assert.DoesNotContain("Projekte ansehen", html);
        Assert.DoesNotContain("Schematische Darstellung", html);
    }

    [Fact]
    public async Task Start_NoCookie_StaysGerman()
    {
        var html = await _factory.CreateClient().GetStringAsync("/");

        Assert.Contains("<html lang=\"de\"", html);
        Assert.Contains(">Demo-Projekt laden</button>", html);
        Assert.Contains(">Projekte ansehen</a>", html);
        Assert.Contains("<figcaption>Schematische Darstellung, keine Messdaten.</figcaption>", html);
        Assert.DoesNotContain("Load demo project", html);
    }

    [Fact]
    public async Task Projekte_InEnglish_ShowsEnglishTitleAndCards()
    {
        await SeedProjektAsync("Sprachtest Projektliste");
        var client = await LanguageTestHelper.CreateEnglishClientAsync(_factory);

        var html = await client.GetStringAsync("/Projekte");

        Assert.Contains("<html lang=\"en\"", html);
        Assert.Contains("<title>Projects · BoreholeHub</title>", WebUtility.HtmlDecode(html));
        Assert.Contains("<h1>Projects</h1>", html);
        Assert.Contains("aria-label=\"Breadcrumb\"", html);
        Assert.Contains("<li><a href=\"/\">Home</a></li>", html);
        Assert.Contains("Created on 02.11.2026", html);
        Assert.Contains("small percussion borehole</span>", html);
        Assert.DoesNotContain("<h1>Projekte</h1>", html);
        Assert.DoesNotContain("Erstellt am", html);
    }

    [Fact]
    public async Task Projekte_NoCookie_StaysGerman()
    {
        await SeedProjektAsync("Sprachtest Projektliste DE");

        var html = await _factory.CreateClient().GetStringAsync("/Projekte");

        Assert.Contains("<html lang=\"de\"", html);
        Assert.Contains("<title>Projekte · BoreholeHub</title>", WebUtility.HtmlDecode(html));
        Assert.Contains("<h1>Projekte</h1>", html);
        Assert.Contains("Erstellt am 02.11.2026", html);
        Assert.DoesNotContain("Created on", html);
    }

    [Fact]
    public async Task Uebersicht_InEnglish_ShowsEnglishTextAndTabBar()
    {
        int id = await SeedProjektAsync("Sprachtest Übersicht");
        var client = await LanguageTestHelper.CreateEnglishClientAsync(_factory);

        var html = await client.GetStringAsync($"/Projekte/{id}");

        Assert.Contains("<html lang=\"en\"", html);
        Assert.Contains("<a class=\"btn btn-primary\" href=\"/Projekte/" + id + "/Karte\">Open map</a>", html);
        Assert.Contains(">PDF report</a>", html);
        Assert.Contains("aria-label=\"Key figures\"", html);
        Assert.Contains("<div class=\"kpi-label\">Max. final depth</div>", html);
        Assert.Contains("<th scope=\"col\" class=\"num\">Final depth [m]</th>", html);
        Assert.Contains("<th scope=\"col\" class=\"num\">Groundwater [m below ground level]</th>", html);
        Assert.Contains("<span class=\"badge badge-krb\">Small percussion borehole</span>", html);
        Assert.Contains("Coordinates: ETRS89 / UTM 32N (EPSG:25832)", html);
        Assert.Contains("<nav class=\"tabs\" aria-label=\"Project sections\">", html);
        Assert.Contains("<a class=\"tab\" href=\"/Projekte/" + id + "\" aria-current=\"page\">Overview</a>", html);
        Assert.Contains(">Map</a>", html);
        Assert.Contains(">Cross-section</a>", html);
        Assert.Contains(">Lab</a>", html);
        Assert.Contains(">Data check</a>", html);
        Assert.Contains(">Data</a>", html);
        Assert.Contains(">Field</a>", html);
        Assert.DoesNotContain("Karte öffnen", html);
        Assert.DoesNotContain("Endteufe [m]", html);
        Assert.DoesNotContain("Labor &amp; Versickerung", html);
        Assert.DoesNotContain("Feldaufnahme", html);
    }

    [Fact]
    public async Task Uebersicht_NoCookie_StaysGermanIncludingTabBar()
    {
        int id = await SeedProjektAsync("Sprachtest Übersicht DE");

        var html = await _factory.CreateClient().GetStringAsync($"/Projekte/{id}");

        Assert.Contains("<html lang=\"de\"", html);
        Assert.Contains(">Karte öffnen</a>", html);
        Assert.Contains("<nav class=\"tabs\" aria-label=\"Projektbereiche\">", html);
        Assert.Contains(">Übersicht</a>", WebUtility.HtmlDecode(html));
        Assert.Contains(">Labor &amp; Versickerung</a>", html);
        Assert.Contains(">Feldaufnahme</a>", html);
        Assert.Contains("<th scope=\"col\" class=\"num\">Endteufe [m]</th>", html);
        Assert.DoesNotContain("Open map", html);
    }

    [Fact]
    public async Task Error_InEnglish_ShowsEnglishTextAndTitle()
    {
        var client = await LanguageTestHelper.CreateEnglishClientAsync(_factory);

        var html = await client.GetStringAsync("/Error");

        Assert.Contains("<html lang=\"en\"", html);
        Assert.Contains("<title>Error · BoreholeHub</title>", WebUtility.HtmlDecode(html));
        Assert.Contains("<p class=\"eyebrow\">Error</p>", html);
        Assert.Contains("<h1>That did not work</h1>", html);
        Assert.Contains("An unexpected error occurred while processing the request. Please try again.", html);
        Assert.Contains(">Go to the home page</a>", html);
        Assert.DoesNotContain("Das hat nicht geklappt", html);
        Assert.DoesNotContain("Zur Startseite", html);
    }

    [Fact]
    public async Task Error_NoCookie_StaysGerman()
    {
        var html = await _factory.CreateClient().GetStringAsync("/Error");

        Assert.Contains("<html lang=\"de\"", html);
        Assert.Contains("<title>Fehler · BoreholeHub</title>", WebUtility.HtmlDecode(html));
        Assert.Contains("<h1>Das hat nicht geklappt</h1>", html);
        Assert.Contains(">Zur Startseite</a>", html);
    }
}
