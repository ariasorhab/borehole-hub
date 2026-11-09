using System.Net;
using BoreholeHub.Core.Entities;
using BoreholeHub.Web.Data;

namespace BoreholeHub.Tests;

public class PruefungPageTests : IClassFixture<WebAppFactory>
{
    private readonly WebAppFactory _factory;

    public PruefungPageTests(WebAppFactory factory) => _factory = factory;

    private async Task<int> SeedAsync()
    {
        int id = 0;
        await _factory.WithDbAsync(async db => id = await DemoDataSeeder.SeedAsync(db));
        return id;
    }

    private async Task<int> NewProjectAsync(string name, params Aufschluss[] aufschluesse)
    {
        int id = 0;
        await _factory.WithDbAsync(async db =>
        {
            var p = new Project { Name = name, Ort = "Test", Auftraggeber = "Test", ErstelltAm = new DateTime(2026, 11, 2) };
            db.Projects.Add(p);
            await db.SaveChangesAsync();
            id = p.Id;
            foreach (var a in aufschluesse)
            {
                a.ProjectId = id;
                db.Aufschluesse.Add(a);
            }

            await db.SaveChangesAsync();
        });
        return id;
    }

    private static Aufschluss Krb(string bezeichnung, double endteufe, params Schicht[] schichten) => new()
    {
        Bezeichnung = bezeichnung,
        Art = AufschlussArt.Kleinrammbohrung,
        Rechtswert = 516000,
        Hochwert = 5640000,
        AnsatzhoeheMNhn = 200,
        EndteufeM = endteufe,
        Datum = new DateOnly(2026, 11, 1),
        Schichten = [.. schichten],
    };

    private static Schicht S(double von, double bis) => new()
    {
        VonM = von, BisM = bis, Kurzzeichen = "U, t", Beschreibung = "Test", Bodengruppe = "TL",
    };

    private async Task<(HttpResponseMessage Response, string Html)> GetPageAsync(int id)
    {
        var response = await _factory.CreateClient().GetAsync($"/Projekte/{id}/Pruefung");
        var html = System.Net.WebUtility.HtmlDecode(await response.Content.ReadAsStringAsync());
        return (response, html);
    }

    [Fact]
    public async Task Pruefung_ReturnsOk_WithActiveTab_AndNote()
    {
        int id = await SeedAsync();
        var (response, html) = await GetPageAsync(id);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Contains($"<a class=\"tab\" href=\"/Projekte/{id}/Pruefung\" aria-current=\"page\">Prüfung</a>", html);
        Assert.Contains("Automatische Plausibilitätsprüfung der erfassten Daten. Sie ersetzt keine fachliche Prüfung.", html);
        Assert.Contains("css/pruefung.css", html);
    }

    [Fact]
    public async Task Pruefung_TabBar_HasPruefungBetweenLaborAndDaten()
    {
        int id = await SeedAsync();
        var (_, html) = await GetPageAsync(id);

        int labor = html.IndexOf($"href=\"/Projekte/{id}/Labor\"", StringComparison.Ordinal);
        int pruefung = html.IndexOf($"href=\"/Projekte/{id}/Pruefung\"", StringComparison.Ordinal);
        int daten = html.IndexOf($"href=\"/Projekte/{id}/Daten\"", StringComparison.Ordinal);
        Assert.InRange(labor, 1, pruefung);
        Assert.InRange(pruefung, labor, daten);
    }

    [Fact]
    public async Task Pruefung_UnknownId_Returns404()
    {
        var response = await _factory.CreateClient().GetAsync("/Projekte/99999/Pruefung");
        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
    }

    [Fact]
    public async Task Pruefung_DemoProject_ShowsThreeKpiLabels()
    {
        int id = await SeedAsync();
        var (_, html) = await GetPageAsync(id);

        Assert.Contains("<div class=\"kpi-label\">Fehler</div>", html);
        Assert.Contains("<div class=\"kpi-label\">Hinweise</div>", html);
        Assert.Contains("<div class=\"kpi-label\">Ohne Befund</div>", html);
        Assert.Contains("<th scope=\"col\">Stufe</th>", html);
        Assert.Contains("<th scope=\"col\">Aufschluss</th>", html);
        Assert.Contains("<th scope=\"col\">Regel</th>", html);
        Assert.Contains("<th scope=\"col\">Meldung</th>", html);
    }

    [Fact]
    public async Task Pruefung_BrokenKleinrammbohrung_ShowsGapMessage_AndLinkToAufschluss()
    {
        var broken = Krb("KRB 9", 3.0, S(0.0, 1.0), S(1.2, 3.0));
        int id = await NewProjectAsync("Prüfung Lücke", broken);

        var (_, html) = await GetPageAsync(id);

        Assert.Contains("Lücke 1,00–1,20 m.", html);
        Assert.Contains($"<a href=\"/Aufschluesse/{broken.Id}\">KRB 9</a>", html);
        Assert.Contains("<span class=\"badge badge-bad\">Fehler</span>", html);
    }

    [Fact]
    public async Task Pruefung_CleanProject_ShowsGreenRow_AndNoFindings()
    {
        var clean = Krb("KRB 1", 3.0, S(0.0, 1.0), S(1.0, 3.0));
        int id = await NewProjectAsync("Prüfung sauber", clean);

        var (_, html) = await GetPageAsync(id);

        Assert.Contains("Alle Prüfungen bestanden.", html);
        Assert.Contains("<span class=\"badge badge-ok\">OK</span>", html);
        Assert.DoesNotContain("<span class=\"badge badge-bad\">Fehler</span>", html);
    }

    [Fact]
    public async Task Pruefung_ProjectWithoutAufschluesse_ShowsEmptyText()
    {
        int id = await NewProjectAsync("Prüfung leer");

        var (response, html) = await GetPageAsync(id);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Contains("Noch keine Aufschlüsse erfasst.", html);
        Assert.DoesNotContain("kpi-label\">Fehler</div>", html);
    }
}
