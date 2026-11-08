using System.Net;
using System.Text.RegularExpressions;
using BoreholeHub.Core.Entities;
using BoreholeHub.Web.Data;
using BoreholeHub.Web.Drawing;
using Microsoft.EntityFrameworkCore;

namespace BoreholeHub.Tests;

public class AufschlussPageTests : IClassFixture<WebAppFactory>
{
    private readonly WebAppFactory _factory;

    public AufschlussPageTests(WebAppFactory factory) => _factory = factory;

    private async Task<(int ProjektId, Dictionary<string, int> Ids)> SeedAsync()
    {
        int pid = 0;
        var ids = new Dictionary<string, int>();
        await _factory.WithDbAsync(async db =>
        {
            pid = await DemoDataSeeder.SeedAsync(db);
            foreach (var a in await db.Aufschluesse.Where(x => x.ProjectId == pid).ToListAsync())
            {
                ids[a.Bezeichnung] = a.Id;
            }
        });
        return (pid, ids);
    }

    private static string Decode(string html) => WebUtility.HtmlDecode(html);

    [Theory]
    [InlineData("KRB 1")]
    [InlineData("KRB 3")]
    [InlineData("DPH 2")]
    public async Task Page_ReturnsOk_WithBreadcrumb_Tabs_AndBackButton(string name)
    {
        var (pid, ids) = await SeedAsync();
        var response = await _factory.CreateClient().GetAsync($"/Aufschluesse/{ids[name]}");
        var html = Decode(await response.Content.ReadAsStringAsync());

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Contains("class=\"tabs\"", html);
        Assert.Contains("aria-current=\"page\">Übersicht", html);
        Assert.Contains($"href=\"/Projekte/{pid}/Karte\">Zurück zur Karte", html);
        Assert.Contains("<svg class=\"bohrprofil\"", html);
        Assert.Contains("<svg class=\"rammdiagramm\"", html);
        Assert.Contains("Darstellung in Anlehnung an DIN 4023 (vereinfacht)", html);
        Assert.Contains(DemoDataSeeder.DemoProjectName, html);
    }

    [Fact]
    public async Task Krb3_ShowsGroundwater_AndNearestProbe_WithDistance()
    {
        var (_, ids) = await SeedAsync();
        var html = Decode(await _factory.CreateClient().GetStringAsync($"/Aufschluesse/{ids["KRB 3"]}"));

        Assert.Contains("GW 2,90 m (", html);
        Assert.Contains("DPH 2 · 2,2 m entfernt", html);
        Assert.Contains("Vereinfachte Vorprüfung: Der kf-Wert ersetzt keine Bemessung nach DWA-A 138-1.", html);
        Assert.Contains("F3 sehr frostempfindlich", html);
        Assert.Contains("2,0 · 10⁻³", html);
        Assert.Contains("href=\"/Projekte/", html);
        Assert.Matches("Labor#probe-\\d+\">Laborwerte", html);
    }

    [Fact]
    public async Task Dph_ShowsNearestBorehole()
    {
        var (_, ids) = await SeedAsync();
        var html = Decode(await _factory.CreateClient().GetStringAsync($"/Aufschluesse/{ids["DPH 2"]}"));

        Assert.Contains("KRB 3 · 2,2 m entfernt", html);
        Assert.Contains("Schichtenverzeichnis KRB 3", html);
    }

    [Fact]
    public async Task UnknownId_Returns404()
    {
        var response = await _factory.CreateClient().GetAsync("/Aufschluesse/99999");
        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
    }

    [Fact]
    public async Task ProjectDetailTable_LinksEveryBezeichnungToAufschlussPage()
    {
        var (pid, ids) = await SeedAsync();
        var html = await _factory.CreateClient().GetStringAsync($"/Projekte/{pid}");

        foreach (var (name, id) in ids)
        {
            Assert.Contains($"<a href=\"/Aufschluesse/{id}\">{name}</a>", html);
        }
    }

    // ---------- SVG classes ----------

    private static Aufschluss Krb(double? gw, params (double von, double bis, string kurz, string gruppe)[] layers)
    {
        var a = new Aufschluss
        {
            Id = 1, Bezeichnung = "KRB 9", Art = AufschlussArt.Kleinrammbohrung, Rechtswert = 516290, Hochwert = 5640622,
            AnsatzhoeheMNhn = 212.7, EndteufeM = layers[^1].bis, GrundwasserM = gw, Datum = new DateOnly(2026, 10, 27),
        };
        foreach (var l in layers)
        {
            a.Schichten.Add(new Schicht { VonM = l.von, BisM = l.bis, Kurzzeichen = l.kurz, Beschreibung = "Beschreibung " + l.kurz, Bodengruppe = l.gruppe, Zustand = "steif" });
        }

        return a;
    }

    [Fact]
    public void BohrprofilSvg_HasOneRectPerLayer_AndGroundwaterText()
    {
        var a = Krb(2.9, (0, 0.3, "U, h, s'", "OH"), (0.3, 1.4, "U, s, t'", "UL"), (1.4, 4.0, "G, s, u'", "GU"));

        string svg = BohrprofilSvg.Render(a, a.Schichten, 4.0);

        Assert.StartsWith("<svg", svg);
        Assert.Contains("viewBox=\"0 0 760 ", svg);
        Assert.Equal(3, Regex.Matches(svg, "<rect class=\"bp-layer\"").Count);
        Assert.Contains("GW 2,90 m (27.10.2026)", svg);
        Assert.Contains("ET 4,00 m", svg);
        Assert.Contains("<title", svg);
    }

    [Fact]
    public void BohrprofilSvg_WithoutGroundwater_HasNoGwText()
    {
        var a = Krb(null, (0, 1.0, "A, U, s, g'", "UL"), (1.0, 3.0, "T, u'", "TL"));

        string svg = BohrprofilSvg.Render(a, a.Schichten, 3.0);

        Assert.DoesNotContain("GW ", svg);
        Assert.Equal(2, Regex.Matches(svg, "<rect class=\"bp-layer\"").Count);
    }

    [Fact]
    public void RammdiagrammSvg_Has40Bars_AndSameHeightAsLog()
    {
        var dph = new Aufschluss { Id = 2, Bezeichnung = "DPH 9", Art = AufschlussArt.Rammsondierung, EndteufeM = 4.0, AnsatzhoeheMNhn = 212.7, Datum = new DateOnly(2026, 10, 28) };
        var werte = Enumerable.Range(1, 40).Select(i => new RammMesswert { TiefeBisM = Math.Round(i * 0.1, 1), N10 = 3 + i / 4 }).ToList();
        var krb = Krb(null, (0, 4.0, "G, s", "GW"));

        string chart = RammdiagrammSvg.Render(dph, werte, 4.0);
        string log = BohrprofilSvg.Render(krb, krb.Schichten, 4.0);

        Assert.Equal(40, Regex.Matches(chart, "<rect class=\"rd-bar\"").Count);
        Assert.Contains("N10 (Schläge je 10 cm)", chart);
        Assert.Contains("DPH 9 · schwere Rammsonde (DIN EN ISO 22476-2)", chart);
        Assert.Equal(Height(chart), Height(log));
        Assert.NotEqual(Height(chart), Height(RammdiagrammSvg.Render(dph, werte, 5.0)));
    }

    private static string Height(string svg) =>
        Regex.Match(svg, "viewBox=\"0 0 [\\d.]+ ([\\d.]+)\"").Groups[1].Value;
}
