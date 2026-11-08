using System.Net;
using BoreholeHub.Core.Entities;

namespace BoreholeHub.Tests;

public class PagesTests : IClassFixture<WebAppFactory>
{
    private const string Footer =
        "Demo mit synthetischen Daten · Mit KI-Unterstützung erstellt, entworfen und geprüft von Sohrab Talebian Far · Version 1.0.0";

    private readonly WebAppFactory _factory;

    public PagesTests(WebAppFactory factory) => _factory = factory;

    private async Task<int> EnsureProjectAsync()
    {
        int id = 0;
        await _factory.WithDbAsync(async db =>
        {
            var existing = db.Projects.FirstOrDefault(p => p.Name == "Testprojekt Seiten");
            if (existing is not null) { id = existing.Id; return; }

            var projekt = new Project
            {
                Name = "Testprojekt Seiten",
                Ort = "Schwalmstadt",
                Auftraggeber = "Muster GmbH (fiktiv)",
                Beschreibung = "Synthetisches Testprojekt.",
                ErstelltAm = new DateTime(2026, 11, 2),
            };
            // Final depths 3.0, 4.0 and 2.5 m: the KPI must show "4,0 m".
            projekt.Aufschluesse.Add(new Aufschluss { Bezeichnung = "KRB 1", Art = AufschlussArt.Kleinrammbohrung, Rechtswert = 512858, Hochwert = 5638728, AnsatzhoeheMNhn = 215.2, EndteufeM = 3.0, GrundwasserM = 2.8, Datum = new DateOnly(2026, 10, 27) });
            projekt.Aufschluesse.Add(new Aufschluss { Bezeichnung = "KRB 2", Art = AufschlussArt.Kleinrammbohrung, Rechtswert = 512882, Hochwert = 5638728, AnsatzhoeheMNhn = 215.0, EndteufeM = 2.5, Datum = new DateOnly(2026, 10, 27) });
            projekt.Aufschluesse.Add(new Aufschluss { Bezeichnung = "DPH 1", Art = AufschlussArt.Rammsondierung, Rechtswert = 512860, Hochwert = 5638730, AnsatzhoeheMNhn = 215.1, EndteufeM = 4.0, Datum = new DateOnly(2026, 10, 28) });
            db.Projects.Add(projekt);
            await db.SaveChangesAsync();
            id = projekt.Id;
        });
        return id;
    }

    [Theory]
    [InlineData("/")]
    [InlineData("/Projekte")]
    public async Task Pages_ReturnOk_AndContainFooter(string url)
    {
        await EnsureProjectAsync();
        var client = _factory.CreateClient();

        var response = await client.GetAsync(url);
        var html = await response.Content.ReadAsStringAsync();

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Contains(Footer, html);
        Assert.Contains("lang=\"de\"", html);
    }

    [Fact]
    public async Task Details_ReturnsOk_WithGermanKpiValues()
    {
        int id = await EnsureProjectAsync();
        var client = _factory.CreateClient();

        var response = await client.GetAsync($"/Projekte/{id}");
        var html = await response.Content.ReadAsStringAsync();

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Contains(Footer, html);
        Assert.Contains("4,0 m", html);
        Assert.Contains("27.10.2026", html);
        Assert.Contains($"/Projekte/{id}/Karte", html);
    }

    [Fact]
    public async Task Details_UnknownId_Returns404_WithNotFoundPage()
    {
        var client = _factory.CreateClient();

        var response = await client.GetAsync("/Projekte/99999");
        var html = await response.Content.ReadAsStringAsync();

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
        Assert.Contains("Seite nicht gefunden", html);
    }

    [Fact]
    public async Task UnknownUrl_Returns404_WithNotFoundPage()
    {
        var client = _factory.CreateClient();

        var response = await client.GetAsync("/gibt-es-nicht");

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
        Assert.Contains("Seite nicht gefunden", await response.Content.ReadAsStringAsync());
    }

    [Fact]
    public async Task EmptyProjectList_ShowsEmptyState()
    {
        using var empty = new WebAppFactory();
        var client = empty.CreateClient();

        var html = await client.GetStringAsync("/Projekte");

        Assert.Contains("Noch keine Projekte vorhanden.", html);
        Assert.Contains("Demo-Projekt laden", html);
    }
}
