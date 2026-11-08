using System.Net;
using BoreholeHub.Core.Entities;
using BoreholeHub.Web.Data;
using BoreholeHub.Web.Drawing;
using Microsoft.EntityFrameworkCore;

namespace BoreholeHub.Tests;

public class ProfilschnittTests : IClassFixture<WebAppFactory>
{
    private readonly WebAppFactory _factory;

    public ProfilschnittTests(WebAppFactory factory) => _factory = factory;

    private async Task<int> SeedAsync()
    {
        int id = 0;
        await _factory.WithDbAsync(async db => id = await DemoDataSeeder.SeedAsync(db));
        return id;
    }

    private async Task<List<Aufschluss>> KrbAsync(int projectId)
    {
        List<Aufschluss> list = [];
        await _factory.WithDbAsync(async db =>
            list = await db.Aufschluesse.AsNoTracking().Include(a => a.Schichten)
                .Where(a => a.ProjectId == projectId && a.Art == AufschlussArt.Kleinrammbohrung).OrderBy(a => a.Id).ToListAsync());
        return list;
    }

    private static string Render(IReadOnlyList<Aufschluss> punkte) =>
        ProfilschnittSvg.Render(punkte, punkte.ToDictionary(a => a.Id, a => (IReadOnlyList<Schicht>)a.Schichten.ToList()));

    [Fact]
    public async Task Page_DemoDefault_ReturnsOk_WithThreeColumnsAndCaption()
    {
        int id = await SeedAsync();
        var response = await _factory.CreateClient().GetAsync($"/Projekte/{id}/Profilschnitt");
        var html = await response.Content.ReadAsStringAsync();

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Equal(3, System.Text.RegularExpressions.Regex.Matches(html, "class=\"ps-column\"").Count);
        Assert.Contains("Profilschnitt KRB 1 – KRB 3 – KRB 5", html);
        Assert.Contains("Überhöhung 1:2", html);
        Assert.Contains("Schematische Interpretation zwischen den Aufschlüssen – keine Messung zwischen den Punkten.", html);
        Assert.Contains("/css/profilschnitt.css", html);
    }

    [Fact]
    public async Task Page_TwoIds_ReturnsOk_WithTwoColumns_AndRepeatedFormFields()
    {
        int id = await SeedAsync();
        var krb = await KrbAsync(id);
        var client = _factory.CreateClient();

        var comma = await client.GetStringAsync($"/Projekte/{id}/Profilschnitt?punkte={krb[0].Id},{krb[1].Id}");
        var repeated = await client.GetStringAsync($"/Projekte/{id}/Profilschnitt?punkte={krb[0].Id}&punkte={krb[1].Id}");

        Assert.Equal(2, System.Text.RegularExpressions.Regex.Matches(comma, "class=\"ps-column\"").Count);
        Assert.Equal(2, System.Text.RegularExpressions.Regex.Matches(repeated, "class=\"ps-column\"").Count);
        Assert.DoesNotContain("role=\"alert\"", comma);
    }

    [Fact]
    public async Task Page_UnknownProject_Returns404()
    {
        var response = await _factory.CreateClient().GetAsync("/Projekte/99999/Profilschnitt");
        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
    }

    [Theory]
    [InlineData("abc")]
    [InlineData("1")]
    [InlineData("1,1")]
    [InlineData("1,99999")]
    [InlineData("1,2,3,4,5")]
    public async Task Page_InvalidIds_ShowsFriendlyMessage_NotAnError(string punkte)
    {
        int id = await SeedAsync();
        var krb = await KrbAsync(id);
        string q = punkte.Replace("1,", krb[0].Id + ",").Replace("2,", krb[1].Id + ",");
        var response = await _factory.CreateClient().GetAsync($"/Projekte/{id}/Profilschnitt?punkte={(punkte == "1" ? krb[0].Id.ToString() : q)}");
        var html = await response.Content.ReadAsStringAsync();

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Contains("role=\"alert\"", html);
        Assert.Contains("Gezeigt wird der Standardschnitt.", html);
    }

    [Fact]
    public async Task Page_DphId_IsInvalid()
    {
        int id = await SeedAsync();
        int dph = 0;
        await _factory.WithDbAsync(async db => dph = await db.Aufschluesse.Where(a => a.ProjectId == id && a.Art == AufschlussArt.Rammsondierung).Select(a => a.Id).FirstAsync());
        var krb = await KrbAsync(id);
        var html = await _factory.CreateClient().GetStringAsync($"/Projekte/{id}/Profilschnitt?punkte={krb[0].Id},{dph}");
        Assert.Contains("role=\"alert\"", html);
    }

    [Fact]
    public async Task Render_OneColumnPerPoint_GroundwaterLine_AndCaption()
    {
        int id = await SeedAsync();
        var krb = await KrbAsync(id);
        var points = new[] { krb[0], krb[2], krb[4] };

        string svg = Render(points);

        Assert.StartsWith("<svg", svg);
        Assert.Equal(1, System.Text.RegularExpressions.Regex.Matches(svg, "<svg").Count);
        Assert.Equal(3, System.Text.RegularExpressions.Regex.Matches(svg, "class=\"ps-column\"").Count);
        Assert.Equal(2, System.Text.RegularExpressions.Regex.Matches(svg, "class=\"ps-gw-line\"").Count); // plot line + legend entry
        Assert.Contains("Schematische Interpretation zwischen den Aufschlüssen – keine Messung zwischen den Punkten.", svg);
        Assert.Contains("Überhöhung 1:2", svg);
        Assert.Contains("<title id=\"ps-title\">Profilschnitt KRB 1 – KRB 3 – KRB 5</title>", svg);
    }

    [Fact]
    public async Task Render_WithoutTwoGroundwaterPoints_HasNoGroundwaterLineInPlot()
    {
        int id = await SeedAsync();
        var krb = await KrbAsync(id);

        // KRB 1 and KRB 2 met no groundwater; the legend entry is the only "ps-gw-line" element then.
        string svg = Render([krb[0], krb[1]]);
        Assert.Equal(1, System.Text.RegularExpressions.Regex.Matches(svg, "class=\"ps-gw-line\"").Count);

        // KRB 3 alone has groundwater: symbol, but still no line in the plot.
        string one = Render([krb[0], krb[2]]);
        Assert.Equal(1, System.Text.RegularExpressions.Regex.Matches(one, "class=\"ps-gw-line\"").Count);
        Assert.Contains("class=\"ps-gw-sym\"", one);

        // Two points with groundwater: the line is drawn in the plot (plus the legend entry).
        string two = Render([krb[2], krb[4]]);
        Assert.Equal(2, System.Text.RegularExpressions.Regex.Matches(two, "class=\"ps-gw-line\"").Count);
    }

    [Fact]
    public void Render_MissingHomogenbereichInNeighbour_WedgesOut()
    {
        Aufschluss P(int id, double r, params (double von, double bis, string hb)[] layers)
        {
            var a = new Aufschluss { Id = id, Bezeichnung = "KRB " + id, Art = AufschlussArt.Kleinrammbohrung, Rechtswert = r, Hochwert = 0, AnsatzhoeheMNhn = 100, EndteufeM = layers[^1].bis };
            foreach (var l in layers)
            {
                a.Schichten.Add(new Schicht { VonM = l.von, BisM = l.bis, Kurzzeichen = "U, s'", Beschreibung = "Schluff", Bodengruppe = "UL", Homogenbereich = l.hb });
            }

            return a;
        }

        var a1 = P(1, 0, (0, 1, "B1"), (1, 2, "B2"), (2, 3, "B3"));
        var a2 = P(2, 20, (0, 1, "B1"), (1, 3, "B3"));
        string svg = Render([a1, a2]);

        Assert.Contains("ps-body ps-wedge\" data-hb=\"B2\"", svg);
        Assert.DoesNotContain("ps-body ps-wedge\" data-hb=\"B1\"", svg);
    }

    [Fact]
    public void Render_FewerThanTwoPoints_Throws()
    {
        var a = new Aufschluss { Id = 1, Bezeichnung = "KRB 1", EndteufeM = 2 };
        Assert.Throws<ArgumentException>(() => Render([a]));
    }

    [Fact]
    public async Task Tabs_ContainProfilschnitt_AfterKarte_AndKartePageLinksToIt()
    {
        int id = await SeedAsync();
        var html = await _factory.CreateClient().GetStringAsync($"/Projekte/{id}");
        int karte = html.IndexOf($"href=\"/Projekte/{id}/Karte\"", StringComparison.Ordinal);
        int schnitt = html.IndexOf($"<a class=\"tab\" href=\"/Projekte/{id}/Profilschnitt\">Profilschnitt</a>", StringComparison.Ordinal);
        Assert.True(karte > 0 && schnitt > karte);

        var profil = await _factory.CreateClient().GetStringAsync($"/Projekte/{id}/Profilschnitt");
        Assert.Contains("aria-current=\"page\">Profilschnitt</a>", profil);

        var kartePage = await _factory.CreateClient().GetStringAsync($"/Projekte/{id}/Karte");
        Assert.Contains($"href=\"/Projekte/{id}/Profilschnitt\">Profilschnitt</a>", kartePage);
    }

    [Fact]
    public void StandardAuswahl_PicksFarthestPair_AndMiddlePoint()
    {
        Aufschluss P(int id, double r, double h) => new() { Id = id, Bezeichnung = "KRB " + id, Rechtswert = r, Hochwert = h };
        var list = new[] { P(1, 0, 20), P(2, 24, 20), P(3, 12, 10), P(4, 0, 0), P(5, 24, 0) };
        var sel = BoreholeHub.Web.Pages.Projekte.ProfilschnittModel.StandardAuswahl(list);
        Assert.Equal(new[] { 1, 3, 5 }, sel.Select(a => a.Id).ToArray());
    }
}
