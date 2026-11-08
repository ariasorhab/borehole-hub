using System.Net;
using System.Text.RegularExpressions;
using BoreholeHub.Core.Services;
using BoreholeHub.Web.Data;
using BoreholeHub.Web.Pages.Projekte;
using Microsoft.EntityFrameworkCore;

namespace BoreholeHub.Tests;

public class LaborPageTests : IClassFixture<WebAppFactory>
{
    private readonly WebAppFactory _factory;

    public LaborPageTests(WebAppFactory factory) => _factory = factory;

    private async Task<int> SeedAsync()
    {
        int id = 0;
        await _factory.WithDbAsync(async db => id = await DemoDataSeeder.SeedAsync(db));
        return id;
    }

    private async Task<string> GetAsync(int id) =>
        WebUtility.HtmlDecode(await _factory.CreateClient().GetStringAsync($"/Projekte/{id}/Labor"));

    [Fact]
    public async Task Labor_ReturnsOk_WithNotice_Tabs_AndSections()
    {
        int id = await SeedAsync();
        var response = await _factory.CreateClient().GetAsync($"/Projekte/{id}/Labor");
        var html = WebUtility.HtmlDecode(await response.Content.ReadAsStringAsync());

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Contains("Beispielwerte zur Demonstration – keine amtlichen Werte der Ersatzbaustoffverordnung (EBV).", html);
        Assert.Contains("aria-current=\"page\">Labor & Versickerung", html);
        Assert.Contains("Versickerung (Vorprüfung)", html);
        Assert.Contains("Nur auffällige Proben", html);
        Assert.Contains("Vereinfachte Vorprüfung: Der kf-Wert ersetzt keine Bemessung nach DWA-A 138-1.", html);
    }

    [Fact]
    public async Task Labor_UnknownId_Returns404()
    {
        var response = await _factory.CreateClient().GetAsync("/Projekte/99999/Labor");
        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
    }

    [Fact]
    public async Task Labor_KpiCounts_MatchLabCheckService()
    {
        int id = await SeedAsync();
        int total = 0, bm0 = 0, higher = 0, exceeded = 0;
        await _factory.WithDbAsync(async db =>
        {
            var limits = await db.Grenzwerte.ToListAsync();
            var proben = await db.Proben.Include(p => p.Aufschluss).Include(p => p.Laborwerte).Where(p => p.Aufschluss.ProjectId == id).ToListAsync();
            foreach (var p in proben)
            {
                total++;
                var results = LabCheckService.Evaluate(p.Laborwerte, limits);
                if (results.Any(r => r.Ueberschritten)) { exceeded++; continue; }
                int worst = results.Max(r => limits.First(g => g.Parameter == r.Parameter && g.Klasse == r.EingehalteneKlasse).Reihenfolge);
                if (worst == 1) { bm0++; } else { higher++; }
            }
        });
        Assert.Equal(6, total);
        Assert.True(higher >= 1, "the demo has at least one sample above BM-0");

        var html = await GetAsync(id);
        string Kpi(string label) => Regex.Match(html, "kpi-value\">(\\d+)</div>\\s*<div class=\"kpi-label\">" + Regex.Escape(label)).Groups[1].Value;
        Assert.Equal(total.ToString(), Kpi("Proben"));
        Assert.Equal(bm0.ToString(), Kpi("Einstufung BM-0"));
        Assert.Equal(higher.ToString(), Kpi("Höhere Klasse"));
        Assert.Equal(exceeded.ToString(), Kpi("Überschreitung"));
    }

    [Fact]
    public async Task Labor_ShowsFillSample_WithBleiClass_AndAnchors()
    {
        int id = await SeedAsync();
        var ids = new List<int>();
        await _factory.WithDbAsync(async db => ids.AddRange(await db.Proben.Where(p => p.Aufschluss.ProjectId == id).Select(p => p.Id).ToListAsync()));
        var html = await GetAsync(id);

        foreach (int probeId in ids)
        {
            Assert.Contains($"id=\"probe-{probeId}\"", html);
        }

        Assert.Contains("KRB 1/1 · 0,30–0,90 m · Auffüllung", html);
        // Blei 185 mg/kg is above BM-F1 (160) and within BM-F2 (250) in the example limits.
        Assert.Matches("<th scope=\"row\">Blei</th>\\s*<td class=\"num\">185</td>\\s*<td>mg/kg</td>\\s*<td><span class=\"badge badge-warn\">BM-F2</span>", html);
        Assert.Contains("class=\"row-warn\"", html);
    }

    [Fact]
    public async Task Labor_SampleAboveAllLimits_ShowsBadBadge_AndCountsAsExceedance()
    {
        await SeedAsync(); // makes sure the example limits exist
        int pid = 0;
        await _factory.WithDbAsync(async db =>
        {
            var projekt = new BoreholeHub.Core.Entities.Project { Name = "Labor-Test Überschreitung", Ort = "Test", Auftraggeber = "Test", ErstelltAm = new DateTime(2026, 11, 2) };
            var krb = new BoreholeHub.Core.Entities.Aufschluss { Bezeichnung = "KRB 7", Art = BoreholeHub.Core.Entities.AufschlussArt.Kleinrammbohrung, Rechtswert = 516290, Hochwert = 5640622, AnsatzhoeheMNhn = 212.7, EndteufeM = 2.0, Datum = new DateOnly(2026, 10, 27) };
            krb.Schichten.Add(new BoreholeHub.Core.Entities.Schicht { VonM = 0, BisM = 2.0, Kurzzeichen = "A, U", Beschreibung = "Auffüllung: Schluff" });
            var probe = new BoreholeHub.Core.Entities.Probe { Bezeichnung = "KRB 7/1", VonM = 0.3, BisM = 1.0 };
            probe.Laborwerte.Add(new BoreholeHub.Core.Entities.Laborwert { Parameter = "Blei", Wert = 420, Einheit = "mg/kg" });
            krb.Proben.Add(probe);
            projekt.Aufschluesse.Add(krb);
            db.Projects.Add(projekt);
            await db.SaveChangesAsync();
            pid = projekt.Id;
        });

        var html = await GetAsync(pid);

        Assert.Matches("<th scope=\"row\">Blei</th>\\s*<td class=\"num\">420</td>\\s*<td>mg/kg</td>\\s*<td><span class=\"badge badge-bad\">> BM-F3</span>", html);
        Assert.Contains("KRB 7/1 · 0,30–1,00 m · Auffüllung", html);
        Assert.Contains("class=\"row-bad\"", html);
        Assert.Matches("kpi-value\">1</div>\\s*<div class=\"kpi-label\">Überschreitung", html);
    }

    [Fact]
    public void LaborModel_ExceedanceBadge_UsesLeastStrictClass()
    {
        // The page shows "> BM-F3" for a value above all limits (text built from LabCheckResult.UeberschritteneGrenze).
        var limits = Enumerable.Range(1, 6).Select(i => new BoreholeHub.Core.Entities.Grenzwert { Parameter = "Blei", Einheit = "mg/kg", Klasse = "BM-" + i, Wert = i * 10, Reihenfolge = i }).ToList();
        var r = LabCheckService.Evaluate([new BoreholeHub.Core.Entities.Laborwert { Parameter = "Blei", Einheit = "mg/kg", Wert = 420 }], limits).Single();
        Assert.True(r.Ueberschritten);
        Assert.Equal("BM-6", r.UeberschritteneGrenze);
    }

    [Fact]
    public async Task Labor_InfiltrationRows_EqualLayersWithKf()
    {
        int id = await SeedAsync();
        int layersWithKf = 0;
        await _factory.WithDbAsync(async db =>
            layersWithKf = await db.Schichten.CountAsync(s => s.Aufschluss.ProjectId == id && s.KfWertMs != null));
        var html = await GetAsync(id);
        string section = html[html.IndexOf("id=\"versickerung-titel\"", StringComparison.Ordinal)..];

        Assert.Equal(layersWithKf, Regex.Matches(section, "<th scope=\"row\">(KRB|DPH) \\d</th>").Count);
        Assert.Contains("kf < 1 · 10⁻⁶ m/s", section);
        Assert.Contains("kf > 1 · 10⁻³ m/s", section);
        Assert.Contains("badge-ok\">Geeignet", section);
    }
}
