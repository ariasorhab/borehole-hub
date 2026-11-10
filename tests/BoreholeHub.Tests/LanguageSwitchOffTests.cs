using System.Net;
using Microsoft.AspNetCore.Mvc.Testing;

namespace BoreholeHub.Tests;

/// <summary>Factory with the DE | EN switch off, as in the shipped appsettings.json (D140).</summary>
public class LanguageSwitchOffFactory : WebAppFactory
{
    protected override bool LanguageSwitch => false;
}

/// <summary>With Features:LanguageSwitch = false the app is German only: no switch, no /Sprache, cookie ignored.</summary>
public class LanguageSwitchOffTests(LanguageSwitchOffFactory factory) : IClassFixture<LanguageSwitchOffFactory>
{
    [Fact]
    public async Task Layout_HasNoLanguageSwitch()
    {
        var html = await factory.CreateClient().GetStringAsync("/");
        Assert.DoesNotContain("lang-switch", html);
        Assert.DoesNotContain("/Sprache", html);
        Assert.Contains("<html lang=\"de\"", html);
    }

    [Fact]
    public async Task Sprache_Returns404()
    {
        var client = factory.CreateClient(new WebApplicationFactoryClientOptions { AllowAutoRedirect = false });
        Assert.Equal(HttpStatusCode.NotFound, (await client.GetAsync("/Sprache")).StatusCode);
    }

    [Fact]
    public async Task EnglishCookie_IsIgnored()
    {
        var client = factory.CreateClient();
        var request = new HttpRequestMessage(HttpMethod.Get, "/Projekte");
        request.Headers.Add("Cookie", "bh-lang=c%3Dde-DE%7Cuic%3Den");
        var html = await (await client.SendAsync(request)).Content.ReadAsStringAsync();
        Assert.Contains("<html lang=\"de\"", html);
        Assert.DoesNotContain(">Projects<", html);
    }
}
