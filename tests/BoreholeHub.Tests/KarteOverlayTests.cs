using BoreholeHub.Web.Data;

namespace BoreholeHub.Tests;

public class KarteOverlayTests : IClassFixture<WebAppFactory>
{
    private readonly WebAppFactory _factory;

    public KarteOverlayTests(WebAppFactory factory) => _factory = factory;

    [Fact]
    public async Task Karte_ContainsLayerCheckboxIds_Labels_AndOpacitySliders()
    {
        int id = 0;
        await _factory.WithDbAsync(async db => id = await DemoDataSeeder.SeedAsync(db));
        var html = await _factory.CreateClient().GetStringAsync($"/Projekte/{id}/Karte");

        foreach (var box in new[] { "layer-gk25", "layer-geologie", "layer-boden" })
        {
            Assert.Contains($"id=\"{box}\"", html);
            Assert.Contains($"id=\"opacity-{box}\"", html);
        }

        Assert.Contains("Geologie (GK 25, HLNUG)", html);
        Assert.Contains("Geologie (GÜK 300, HLNUG)", html);
        Assert.Contains("Boden (BFD 50, HLNUG)", html);
        Assert.Contains($"href=\"/Projekte/{id}/Profilschnitt\">Profilschnitt</a>", html);
    }

    [Fact]
    public async Task KarteScript_ContainsGk25ExportUrl_AndIdentify()
    {
        var js = await _factory.CreateClient().GetStringAsync("/js/karte.js");

        Assert.Contains("geodienste-umwelt.hessen.de/arcgis/rest/services/geologie/gk25/MapServer", js);
        Assert.Contains("/export?", js);
        Assert.Contains("/identify?", js);
        Assert.Contains("'layer-gk25'", js);
        Assert.Contains("GK25_OPACITY = 0.6", js);
    }
}
