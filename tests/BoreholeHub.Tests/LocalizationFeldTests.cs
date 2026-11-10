using System.Net;
using System.Text.RegularExpressions;
using BoreholeHub.Core.Entities;
using BoreholeHub.Web.Data;

namespace BoreholeHub.Tests;

/// <summary>DE/EN checks for the Feld page: page texts, data- attributes for the script, validation texts (T47).</summary>
public class LocalizationFeldTests : IClassFixture<WebAppFactory>
{
    private readonly WebAppFactory _factory;

    public LocalizationFeldTests(WebAppFactory factory) => _factory = factory;

    private async Task<int> NewProjectAsync(string name)
    {
        int id = 0;
        await _factory.WithDbAsync(async db =>
        {
            var p = new Project { Name = name, Ort = "Test", Auftraggeber = "Test", ErstelltAm = new DateTime(2026, 11, 2) };
            db.Projects.Add(p);
            await db.SaveChangesAsync();
            id = p.Id;
        });
        return id;
    }

    /// <summary>A KRB form with one layer; the overrides replace single fields.</summary>
    private static List<KeyValuePair<string, string>> KrbFelder(params (string Key, string Value)[] overrides)
    {
        var fields = new List<KeyValuePair<string, string>>
        {
            new("Art", "KRB"), new("Bezeichnung", "KRB 7"), new("Datum", "2026-11-03"),
            new("Rechtswert", "516296,0"), new("Hochwert", "5640626,0"), new("Ansatzhoehe", "212,8"), new("Endteufe", "3,0"),
            new("Grundwasser", ""), new("Bemerkung", ""),
            new("Schichten[0].Von", "0,00"), new("Schichten[0].Bis", "0,40"), new("Schichten[0].Kurzzeichen", "U, h"),
            new("Schichten[0].Beschreibung", "Mutterboden"), new("Schichten[0].Bodengruppe", "OH"), new("Schichten[0].Kf", "1e-6"),
        };
        foreach (var (key, value) in overrides)
        {
            fields.RemoveAll(f => f.Key == key);
            fields.Add(new(key, value));
        }

        return fields;
    }

    /// <summary>Loads the page (for the antiforgery token) and posts the form like a browser does.</summary>
    private static async Task<HttpResponseMessage> PostFeldAsync(HttpClient client, int id, IEnumerable<KeyValuePair<string, string>> fields)
    {
        var page = await client.GetStringAsync($"/Projekte/{id}/Feld");
        var token = Regex.Match(page, "name=\"__RequestVerificationToken\"[^>]*value=\"([^\"]+)\"").Groups[1].Value;
        Assert.NotEmpty(token);
        var all = fields.ToList();
        all.Add(new("__RequestVerificationToken", token));
        return await client.PostAsync($"/Projekte/{id}/Feld", new FormUrlEncodedContent(all));
    }

    [Fact]
    public async Task Feld_English_ShowsEnglishPage_AndNoGermanHeadings()
    {
        int id = await NewProjectAsync("Feld englisch");
        var client = await LanguageTestHelper.CreateEnglishClientAsync(_factory);

        var html = WebUtility.HtmlDecode(await client.GetStringAsync($"/Projekte/{id}/Feld"));

        Assert.Contains("<html lang=\"en\"", html);
        Assert.Contains("<h1>Field log</h1>", html);
        Assert.Contains("Use my location", html);
        Assert.Contains("Remove", html);
        Assert.Contains("Save", html);
        Assert.DoesNotContain("<h1>Feldaufnahme</h1>", html);
        Assert.DoesNotContain("Standort übernehmen", html);
        Assert.DoesNotContain(">Entfernen<", html);
    }

    [Fact]
    public async Task Feld_English_DataAttributes_CarryTheScriptTextsInEnglish()
    {
        int id = await NewProjectAsync("Feld Attribute");
        var client = await LanguageTestHelper.CreateEnglishClientAsync(_factory);

        var html = WebUtility.HtmlDecode(await client.GetStringAsync($"/Projekte/{id}/Feld"));

        Assert.Contains("data-msg-standort-nicht-verfuegbar=\"Location not available – please enter coordinates.\"", html);
        Assert.Contains("data-msg-standort-ermittelt=\"Getting location …\"", html);
        Assert.Contains("data-msg-genauigkeit=\"Accuracy ± {0} m\"", html);
        Assert.Contains("data-msg-n10-bis=\"N10 up to {0} m\"", html);
        Assert.Contains("aria-label=\"N10 up to 0,10 m\"", html);
    }

    [Fact]
    public async Task Feld_English_PostWithErrors_ShowsEnglishValidationTexts()
    {
        int id = await NewProjectAsync("Feld Fehler englisch");
        var client = await LanguageTestHelper.CreateEnglishClientAsync(_factory);

        var response = await PostFeldAsync(client, id, KrbFelder(("Endteufe", ""), ("Schichten[0].Bis", "x")));
        var html = WebUtility.HtmlDecode(await response.Content.ReadAsStringAsync());

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Contains("Nothing saved", html);
        Assert.Contains("Final depth: please enter a number.", html);
        Assert.Contains("Layer 1, to: please enter a number.", html);
        Assert.DoesNotContain("Endteufe: Bitte eine Zahl eingeben.", html);
        Assert.DoesNotContain("Nichts gespeichert", html);
    }

    [Fact]
    public async Task Feld_German_PostWithErrors_StaysGerman()
    {
        int id = await NewProjectAsync("Feld Fehler deutsch");
        var client = _factory.CreateClient(new Microsoft.AspNetCore.Mvc.Testing.WebApplicationFactoryClientOptions { AllowAutoRedirect = false });

        var response = await PostFeldAsync(client, id, KrbFelder(("Endteufe", ""), ("Schichten[0].Bis", "x")));
        var html = WebUtility.HtmlDecode(await response.Content.ReadAsStringAsync());

        Assert.Contains("<html lang=\"de\"", html);
        Assert.Contains("Nichts gespeichert (2 Fehler)", html);
        Assert.Contains("Endteufe: Bitte eine Zahl eingeben.", html);
        Assert.Contains("Schicht 1, Bis: Bitte eine Zahl eingeben.", html);
        Assert.DoesNotContain("Final depth", html);
    }

    [Fact]
    public async Task Feld_German_StaysGerman_WithoutCookie()
    {
        int id = await NewProjectAsync("Feld deutsch");
        var client = _factory.CreateClient();

        var html = WebUtility.HtmlDecode(await client.GetStringAsync($"/Projekte/{id}/Feld"));

        Assert.Contains("<html lang=\"de\"", html);
        Assert.Contains("<h1>Feldaufnahme</h1>", html);
        Assert.Contains("Standort übernehmen", html);
        Assert.Contains(">Entfernen<", html);
        Assert.Contains("data-msg-genauigkeit=\"Genauigkeit ± {0} m\"", html);
        Assert.Contains("aria-label=\"N10 bis 0,10 m\"", html);
    }
}
