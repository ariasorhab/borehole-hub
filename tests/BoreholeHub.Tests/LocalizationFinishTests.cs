using System.Net;
using BoreholeHub.Core.Entities;
using BoreholeHub.Web.Data;
using Microsoft.EntityFrameworkCore;

namespace BoreholeHub.Tests;

/// <summary>Sprint 12b finish (T49): cross-section legend, shared texts, Ground level and the Labor guard.</summary>
public class LocalizationFinishTests : IClassFixture<WebAppFactory>
{
    private readonly WebAppFactory _factory;

    public LocalizationFinishTests(WebAppFactory factory) => _factory = factory;

    private async Task<int> SeedDemoAsync()
    {
        int id = 0;
        await _factory.WithDbAsync(async db => id = await DemoDataSeeder.SeedAsync(db));
        return id;
    }

    private static string Decode(string html) => WebUtility.HtmlDecode(html);

    private async Task<int> KrbIdAsync(int projectId)
    {
        int id = 0;
        await _factory.WithDbAsync(async db =>
            id = (await db.Aufschluesse.Where(a => a.ProjectId == projectId && a.Bezeichnung == "KRB 3").ToListAsync()).Single().Id);
        return id;
    }

    [Fact]
    public async Task CrossSectionLegend_InEnglish_ShowsEnglishSoilNames()
    {
        int id = await SeedDemoAsync();
        var client = await LanguageTestHelper.CreateEnglishClientAsync(_factory);

        var html = Decode(await client.GetStringAsync($"/Projekte/{id}/Profilschnitt"));

        Assert.Contains("<html lang=\"en\"", html);
        Assert.Contains(">Made ground: Silt, sandy</text>", html);
        Assert.Contains(">Topsoil</text>", html);
        Assert.Contains(">Gravel, sandy</text>", html);
        Assert.Contains(">Silt, clayey</text>", html);
        Assert.DoesNotContain(">Auffüllung: Schluff, sandig</text>", html);
        Assert.DoesNotContain(">Mutterboden</text>", html);
    }

    [Fact]
    public async Task CrossSectionLegend_WithoutCookie_StaysGerman()
    {
        int id = await SeedDemoAsync();
        var client = _factory.CreateClient();

        var html = Decode(await client.GetStringAsync($"/Projekte/{id}/Profilschnitt"));

        Assert.Contains("<html lang=\"de\"", html);
        Assert.Contains(">Auffüllung: Schluff, sandig</text>", html);
        Assert.Contains(">Mutterboden</text>", html);
        Assert.Contains(">Kies, sandig</text>", html);
    }

    [Fact]
    public async Task SharedKey_Projekte_RendersProjects_OnTwoPages_InEnglish()
    {
        int id = await SeedDemoAsync();
        var client = await LanguageTestHelper.CreateEnglishClientAsync(_factory);

        var list = Decode(await client.GetStringAsync("/Projekte"));
        var karte = Decode(await client.GetStringAsync($"/Projekte/{id}/Karte"));

        Assert.Contains("<h1>Projects</h1>", list);
        Assert.Contains("<li><a href=\"/Projekte\">Projects</a></li>", karte);
        Assert.Contains("aria-label=\"Breadcrumb\"", karte);
        Assert.DoesNotContain("aria-label=\"Brotkrumen\"", karte);
    }

    [Fact]
    public async Task GroundLevel_OnEnglishPages_ReplacesAnsatzhoehe()
    {
        int id = await SeedDemoAsync();
        int krb3 = await KrbIdAsync(id);
        var client = await LanguageTestHelper.CreateEnglishClientAsync(_factory);

        var detail = Decode(await client.GetStringAsync($"/Projekte/{id}"));
        var log = Decode(await client.GetStringAsync($"/Aufschluesse/{krb3}"));

        Assert.Contains("<th scope=\"col\" class=\"num\">Ground level [m NHN]</th>", detail);
        Assert.Contains("Ground level ", log);
        Assert.DoesNotContain("Ansatzhöhe", detail);
        Assert.DoesNotContain("Ansatzhöhe", log);
    }

    [Fact]
    public async Task Labor_ProjectWithoutLimitValues_Returns200()
    {
        // A fresh factory has no Grenzwerte, so the legend badge that used Klassen[^1] would throw here.
        using var factory = new WebAppFactory();
        int projectId = 0;
        await factory.WithDbAsync(async db =>
        {
            var project = new Project { Name = "Ohne Grenzwerte", Ort = "Testort", Auftraggeber = "Test", ErstelltAm = new DateTime(2026, 11, 10) };
            db.Projects.Add(project);
            await db.SaveChangesAsync();
            projectId = project.Id;
        });

        var response = await factory.CreateClient().GetAsync($"/Projekte/{projectId}/Labor");
        var html = Decode(await response.Content.ReadAsStringAsync());

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Contains("Für dieses Projekt sind noch keine Proben erfasst.", html);
        Assert.DoesNotContain("Überschreitung aller Klassen", html);
    }

    [Fact]
    public async Task Labor_DemoProject_StaysGerman_WithLimitBadge()
    {
        int id = await SeedDemoAsync();

        var html = Decode(await _factory.CreateClient().GetStringAsync($"/Projekte/{id}/Labor"));

        Assert.Contains("<html lang=\"de\"", html);
        Assert.Contains("Beispielklassen (streng bis weniger streng):", html);
        Assert.Contains("= Überschreitung aller Klassen", html);
        Assert.Contains("Laborwerte", html);
        Assert.Contains("Ersatzbaustoffverordnung (EBV)", html);
    }
}
