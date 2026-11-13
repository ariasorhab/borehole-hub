using System.Globalization;
using System.Net;
using BoreholeHub.Core.Entities;
using BoreholeHub.Web.Pages;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Localization;

namespace BoreholeHub.Tests;

public class LanguageSwitchTests : IClassFixture<WebAppFactory>
{
    private const string FooterDe =
        "Demo mit synthetischen Daten · Mit KI-Unterstützung erstellt, entworfen und geprüft von Sohrab Talebian Far · Version 1.0.2";
    private const string FooterEn =
        "Demo with synthetic data · Created with AI assistance, designed and checked by Sohrab Talebian Far · Version 1.0.2";

    private readonly WebAppFactory _factory;

    public LanguageSwitchTests(WebAppFactory factory) => _factory = factory;

    [Fact]
    public async Task NoCookie_RendersGerman_WithGermanButtonActive()
    {
        var html = await _factory.CreateClient().GetStringAsync("/");

        Assert.Contains("<html lang=\"de\"", html);
        Assert.Contains("<a href=\"/Projekte\">Projekte</a>", html);
        Assert.Contains(FooterDe, html);
        Assert.Contains("<button type=\"submit\" name=\"lang\" value=\"de\" lang=\"de\" aria-label=\"Deutsch\" class=\"is-active\" aria-pressed=\"true\">DE</button>", html);
        Assert.Contains("<button type=\"submit\" name=\"lang\" value=\"en\" lang=\"en\" aria-label=\"English\" aria-pressed=\"false\">EN</button>", html);
    }

    [Fact]
    public async Task PostEnglish_WithValidToken_RedirectsToLocalReturnUrl_AndSetsCookie()
    {
        var client = _factory.CreateClient(new Microsoft.AspNetCore.Mvc.Testing.WebApplicationFactoryClientOptions { AllowAutoRedirect = false });
        string token = await LanguageTestHelper.GetAntiForgeryTokenAsync(client);

        var response = await client.PostAsync("/Sprache", new FormUrlEncodedContent(new Dictionary<string, string>
        {
            ["lang"] = "en",
            ["returnUrl"] = "/Projekte",
            ["__RequestVerificationToken"] = token,
        }));

        Assert.Equal(HttpStatusCode.Redirect, response.StatusCode);
        Assert.Equal("/Projekte", response.Headers.Location?.OriginalString);
        string setCookie = Uri.UnescapeDataString(string.Join(";", response.Headers.GetValues("Set-Cookie")));
        Assert.Contains("bh-lang=", setCookie);
        Assert.Contains("uic=en", setCookie);
    }

    [Fact]
    public async Task WithCookie_RendersEnglishLayout_WithEnglishButtonActive()
    {
        var client = await LanguageTestHelper.CreateEnglishClientAsync(_factory);

        var html = await client.GetStringAsync("/");

        Assert.Contains("<html lang=\"en\"", html);
        Assert.Contains("<a href=\"/Projekte\">Projects</a>", html);
        Assert.Contains("Skip to content", html);
        Assert.Contains(FooterEn, html);
        Assert.Contains("<button type=\"submit\" name=\"lang\" value=\"en\" lang=\"en\" aria-label=\"English\" class=\"is-active\" aria-pressed=\"true\">EN</button>", html);
        Assert.Contains("data-label-on=\"Turn dark mode on\"", html);
        Assert.Contains("data-label-off=\"Turn dark mode off\"", html);
    }

    [Fact]
    public async Task NotFound_InEnglish_ShowsEnglishTextAnd404()
    {
        var client = await LanguageTestHelper.CreateEnglishClientAsync(_factory);

        var response = await client.GetAsync("/gibt-es-nicht");
        var html = await response.Content.ReadAsStringAsync();

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
        Assert.Contains("<html lang=\"en\"", html);
        Assert.Contains("<h1>Page not found</h1>", html);
        Assert.Contains("Error 404", html);
        Assert.Contains("This address does not exist, or the project has been removed.", html);
        Assert.Contains("Go to projects", html);
        Assert.Contains("<title>Page not found · BoreholeHub</title>", System.Net.WebUtility.HtmlDecode(html));
    }

    [Fact]
    public async Task PostUnknownLanguage_ReturnsBadRequest()
    {
        var client = _factory.CreateClient(new Microsoft.AspNetCore.Mvc.Testing.WebApplicationFactoryClientOptions { AllowAutoRedirect = false });
        string token = await LanguageTestHelper.GetAntiForgeryTokenAsync(client);

        var response = await client.PostAsync("/Sprache", new FormUrlEncodedContent(new Dictionary<string, string>
        {
            ["lang"] = "fr",
            ["returnUrl"] = "/",
            ["__RequestVerificationToken"] = token,
        }));

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    [Fact]
    public async Task PostExternalReturnUrl_RedirectsToStartPage()
    {
        var client = _factory.CreateClient(new Microsoft.AspNetCore.Mvc.Testing.WebApplicationFactoryClientOptions { AllowAutoRedirect = false });
        string token = await LanguageTestHelper.GetAntiForgeryTokenAsync(client);

        var response = await client.PostAsync("/Sprache", new FormUrlEncodedContent(new Dictionary<string, string>
        {
            ["lang"] = "de",
            ["returnUrl"] = "https://example.com/x",
            ["__RequestVerificationToken"] = token,
        }));

        Assert.Equal(HttpStatusCode.Redirect, response.StatusCode);
        Assert.Equal("/", response.Headers.Location?.OriginalString);
    }

    [Fact]
    public async Task EnglishMode_KeepsGermanDecimalFormat()
    {
        int id = 0;
        await _factory.WithDbAsync(async db =>
        {
            var projekt = new Project
            {
                Name = "Sprachtest Zahlenformat",
                Ort = "Schwalmstadt",
                Auftraggeber = "Muster GmbH (fiktiv)",
                Beschreibung = "Synthetisches Testprojekt.",
                ErstelltAm = new DateTime(2026, 11, 2),
            };
            projekt.Aufschluesse.Add(new Aufschluss { Bezeichnung = "DPH 1", Art = AufschlussArt.Rammsondierung, Rechtswert = 512860, Hochwert = 5638730, AnsatzhoeheMNhn = 215.1, EndteufeM = 4.0, Datum = new DateOnly(2026, 10, 28) });
            db.Projects.Add(projekt);
            await db.SaveChangesAsync();
            id = projekt.Id;
        });
        var client = await LanguageTestHelper.CreateEnglishClientAsync(_factory);

        var html = await client.GetStringAsync($"/Projekte/{id}");

        Assert.Contains("<html lang=\"en\"", html);
        Assert.Contains("4,0 m", html);
        Assert.DoesNotContain("4.0 m", html);
    }

    [Fact]
    public void CSharpClassText_LoadsEnglishResource_ForEnglishUi_AndGermanKeyOtherwise()
    {
        using var scope = _factory.Services.CreateScope();
        var localizer = scope.ServiceProvider.GetRequiredService<IStringLocalizer<NotFoundModel>>();
        const string key = "Beispieltext aus einer C#-Klasse";
        var previous = CultureInfo.CurrentUICulture;
        try
        {
            CultureInfo.CurrentUICulture = new CultureInfo("en");
            Assert.Equal("Example text from a C# class", localizer[key].Value);

            CultureInfo.CurrentUICulture = new CultureInfo("de-DE");
            Assert.Equal(key, localizer[key].Value);
        }
        finally
        {
            CultureInfo.CurrentUICulture = previous;
        }
    }
}
