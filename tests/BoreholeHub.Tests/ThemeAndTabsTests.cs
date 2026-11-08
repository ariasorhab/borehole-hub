using System.Net;
using System.Text.RegularExpressions;
using BoreholeHub.Web.Data;

namespace BoreholeHub.Tests;

public class ThemeAndTabsTests : IClassFixture<WebAppFactory>
{
    private readonly WebAppFactory _factory;

    public ThemeAndTabsTests(WebAppFactory factory) => _factory = factory;

    private async Task<int> SeedAsync()
    {
        int id = 0;
        await _factory.WithDbAsync(async db => id = await DemoDataSeeder.SeedAsync(db));
        return id;
    }

    [Fact]
    public async Task Layout_ContainsThemeToggle_AndInlineThemeScript()
    {
        var html = await _factory.CreateClient().GetStringAsync("/");

        Assert.Contains("<button type=\"button\" class=\"theme-toggle\" id=\"theme-toggle\"", html);
        Assert.Contains("aria-label=\"Dunkelmodus ein\"", html);
        Assert.Contains("data-theme=\"light\"", html);
        Assert.Contains("bh-theme", html);
        Assert.Contains("prefers-color-scheme: dark", html);
        // The script that sets the theme runs in <head>, before the first paint.
        int head = html.IndexOf("</head>", StringComparison.Ordinal);
        int script = html.IndexOf("localStorage.getItem(\"bh-theme\")", StringComparison.Ordinal);
        Assert.InRange(script, 1, head);
    }

    [Theory]
    [InlineData("", "uebersicht", "Übersicht")]
    [InlineData("/Karte", "karte", "Karte")]
    public async Task ProjectPages_ContainTabBar_WithCurrentTabMarked(string suffix, string aktiv, string text)
    {
        int id = await SeedAsync();
        var response = await _factory.CreateClient().GetAsync($"/Projekte/{id}{suffix}");
        var html = await response.Content.ReadAsStringAsync();

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Contains("class=\"tabs\"", html);
        foreach (var url in new[] { $"/Projekte/{id}\"", $"/Projekte/{id}/Karte\"", $"/Projekte/{id}/Labor\"", $"/Projekte/{id}/Daten\"" })
        {
            Assert.Contains("href=\"" + url, html);
        }

        var current = Regex.Matches(html, "<a class=\"tab\" href=\"([^\"]+)\" aria-current=\"page\">([^<]+)</a>");
        Assert.Single(current);
        Assert.Equal(text, WebUtility.HtmlDecode(current[0].Groups[2].Value));
        Assert.Contains(aktiv == "karte" ? "/Karte" : $"/Projekte/{id}", current[0].Groups[1].Value);
        Assert.Contains("Labor &amp; Versickerung", html);
    }
}
