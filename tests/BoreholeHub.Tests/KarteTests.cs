using System.Net;
using System.Text.Json;
using System.Text.RegularExpressions;
using BoreholeHub.Web.Data;

namespace BoreholeHub.Tests;

public class KarteTests : IClassFixture<WebAppFactory>
{
    private readonly WebAppFactory _factory;

    public KarteTests(WebAppFactory factory) => _factory = factory;

    private async Task<int> SeedAsync()
    {
        int id = 0;
        await _factory.WithDbAsync(async db => id = await DemoDataSeeder.SeedAsync(db));
        return id;
    }

    private static async Task<string> GetTokenAsync(HttpClient client)
    {
        var html = await client.GetStringAsync("/");
        var match = Regex.Match(html, "name=\"__RequestVerificationToken\"[^>]*value=\"([^\"]+)\"");
        Assert.True(match.Success, "antiforgery token not found on the landing page");
        return match.Groups[1].Value;
    }

    private HttpClient NoRedirectClient() =>
        _factory.CreateClient(new Microsoft.AspNetCore.Mvc.Testing.WebApplicationFactoryClientOptions { AllowAutoRedirect = false });

    [Fact]
    public async Task Karte_SeededProject_ReturnsOk_WithMapAndLeaflet()
    {
        int id = await SeedAsync();
        var client = _factory.CreateClient();

        var response = await client.GetAsync($"/Projekte/{id}/Karte");
        var html = await response.Content.ReadAsStringAsync();

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Contains("Koordinaten: ETRS89 / UTM 32N (EPSG:25832)", html);
        Assert.Contains("Geologie (GÜK 300, HLNUG)", html);
        Assert.Contains("Boden (BFD 50, HLNUG)", html);
        Assert.Contains("/lib/leaflet/leaflet.js", html);
        Assert.Contains($"/Projekte/{id}/Karte?handler=Punkte", html);
        Assert.Contains("Kleinrammbohrungen", html);
        Assert.Contains("Rammsondierungen", html);
    }

    [Fact]
    public async Task KarteScript_ShowsLabelsOnlyWhenZoomedIn_AndLinksToProfile()
    {
        var js = await _factory.CreateClient().GetStringAsync("/js/karte.js");

        Assert.Contains("LABEL_MIN_ZOOM = 17", js);
        Assert.Contains("closeTooltip", js);
        Assert.Contains("Profil öffnen", js);
        Assert.Contains("'/Aufschluesse/' + p.id", js);
    }

    [Fact]
    public async Task Karte_UnknownId_Returns404()
    {
        var client = _factory.CreateClient();

        var page = await client.GetAsync("/Projekte/99999/Karte");
        var json = await client.GetAsync("/Projekte/99999/Karte?handler=Punkte");

        Assert.Equal(HttpStatusCode.NotFound, page.StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, json.StatusCode);
    }

    [Fact]
    public async Task Punkte_ReturnsNineFeatures_WithExpectedProperties()
    {
        int id = await SeedAsync();
        var client = _factory.CreateClient();

        var response = await client.GetAsync($"/Projekte/{id}/Karte?handler=Punkte");
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        using var doc = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        var root = doc.RootElement;

        Assert.Equal("FeatureCollection", root.GetProperty("type").GetString());
        var features = root.GetProperty("features");
        Assert.Equal(9, features.GetArrayLength());

        string[] expected = ["id", "bezeichnung", "art", "endteufeM", "grundwasserM", "ansatzhoeheMNhn", "rechtswert", "hochwert"];
        int krb = 0, dph = 0, withWater = 0;
        foreach (var f in features.EnumerateArray())
        {
            Assert.Equal("Feature", f.GetProperty("type").GetString());
            var geometry = f.GetProperty("geometry");
            Assert.Equal("Point", geometry.GetProperty("type").GetString());
            var coordinates = geometry.GetProperty("coordinates");
            Assert.Equal(2, coordinates.GetArrayLength());
            double lon = coordinates[0].GetDouble(), lat = coordinates[1].GetDouble();
            Assert.InRange(lon, 9.231739 - 0.002, 9.231739 + 0.002);
            Assert.InRange(lat, 50.917012 - 0.002, 50.917012 + 0.002);

            var props = f.GetProperty("properties");
            Assert.Equal(expected.OrderBy(x => x), props.EnumerateObject().Select(p => p.Name).OrderBy(x => x));
            Assert.Equal(JsonValueKind.Number, props.GetProperty("endteufeM").ValueKind);
            Assert.Equal(JsonValueKind.Number, props.GetProperty("rechtswert").ValueKind);

            switch (props.GetProperty("art").GetString())
            {
                case "Kleinrammbohrung": krb++; break;
                case "Rammsondierung": dph++; break;
                default: Assert.Fail("unexpected art"); break;
            }

            var gw = props.GetProperty("grundwasserM");
            if (gw.ValueKind == JsonValueKind.Number) { withWater++; }
            else { Assert.Equal(JsonValueKind.Null, gw.ValueKind); }
        }

        Assert.Equal(6, krb);
        Assert.Equal(3, dph);
        Assert.Equal(2, withWater);
    }

    [Fact]
    public async Task PostDemo_RedirectsToKarte_AndSeedingTwiceGivesSameId()
    {
        var client = NoRedirectClient();
        var token = await GetTokenAsync(client);
        var form = new Dictionary<string, string> { ["__RequestVerificationToken"] = token };

        var first = await client.PostAsync("/Demo", new FormUrlEncodedContent(form));
        var second = await client.PostAsync("/Demo", new FormUrlEncodedContent(form));

        Assert.Equal(HttpStatusCode.Redirect, first.StatusCode);
        Assert.Equal(HttpStatusCode.Redirect, second.StatusCode);
        var location = first.Headers.Location!.ToString();
        Assert.Matches(@"^/Projekte/\d+/Karte$", location);
        Assert.Equal(location, second.Headers.Location!.ToString());

        // The redirect target renders.
        var map = await _factory.CreateClient().GetAsync(location);
        Assert.Equal(HttpStatusCode.OK, map.StatusCode);
    }

    [Fact]
    public async Task PostDemo_WithoutAntiforgeryToken_IsRejected()
    {
        var client = NoRedirectClient();

        var response = await client.PostAsync("/Demo", new FormUrlEncodedContent(new Dictionary<string, string>()));

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    [Fact]
    public async Task GetDemo_RedirectsToStart()
    {
        var client = NoRedirectClient();

        var response = await client.GetAsync("/Demo");

        Assert.Equal(HttpStatusCode.Redirect, response.StatusCode);
        Assert.Equal("/", response.Headers.Location!.ToString());
    }
}
