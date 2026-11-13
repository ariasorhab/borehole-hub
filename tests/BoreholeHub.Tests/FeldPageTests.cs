using System.Net;
using System.Text.Json;
using System.Text.RegularExpressions;
using BoreholeHub.Core.Entities;
using BoreholeHub.Web.Data;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.EntityFrameworkCore;

namespace BoreholeHub.Tests;

public class FeldPageTests : IClassFixture<WebAppFactory>
{
    private readonly WebAppFactory _factory;

    public FeldPageTests(WebAppFactory factory) => _factory = factory;

    private HttpClient NewClient() => _factory.CreateClient(new WebApplicationFactoryClientOptions { AllowAutoRedirect = false });

    private async Task<int> SeedAsync()
    {
        int id = 0;
        await _factory.WithDbAsync(async db => id = await DemoDataSeeder.SeedAsync(db));
        return id;
    }

    /// <summary>An empty project, so that a test can save "KRB 7" without touching the shared demo project.</summary>
    private async Task<int> NewProjectAsync()
    {
        int id = 0;
        await _factory.WithDbAsync(async db =>
        {
            var p = new Project { Name = "Feld-Test " + Guid.NewGuid().ToString("N")[..6], Ort = "Test", Auftraggeber = "Test", ErstelltAm = new DateTime(2026, 11, 2) };
            db.Projects.Add(p);
            await db.SaveChangesAsync();
            id = p.Id;
        });
        return id;
    }

    private static string Decode(string html) => WebUtility.HtmlDecode(html);

    private static async Task<string> TokenAsync(HttpClient client, int id)
    {
        var page = await client.GetStringAsync($"/Projekte/{id}/Feld");
        var token = Regex.Match(page, "name=\"__RequestVerificationToken\"[^>]*value=\"([^\"]+)\"").Groups[1].Value;
        Assert.NotEmpty(token);
        return token;
    }

    private static List<KeyValuePair<string, string>> Krb7(params (string Key, string Value)[] overrides)
    {
        var fields = new List<KeyValuePair<string, string>>
        {
            new("Art", "KRB"), new("Bezeichnung", "KRB 7"), new("Datum", "2026-11-03"),
            new("Rechtswert", "516296,0"), new("Hochwert", "5640626,0"), new("Ansatzhoehe", "212,8"), new("Endteufe", "3,0"),
            new("Grundwasser", ""), new("Bemerkung", ""),
            new("Schichten[0].Von", "0,00"), new("Schichten[0].Bis", "0,40"), new("Schichten[0].Kurzzeichen", "U, h"),
            new("Schichten[0].Beschreibung", "Mutterboden"), new("Schichten[0].Bodengruppe", "OH"), new("Schichten[0].Kf", "1e-6"),
            new("Schichten[1].Von", "0,40"), new("Schichten[1].Bis", "3,00"), new("Schichten[1].Kurzzeichen", "U, t"),
            new("Schichten[1].Beschreibung", "Auenlehm"), new("Schichten[1].Bodengruppe", "TL"), new("Schichten[1].Kf", "5E-08"),
            // third row stays completely empty and must be ignored
            new("Schichten[2].Von", ""), new("Schichten[2].Bis", ""), new("Schichten[2].Kurzzeichen", ""), new("Schichten[2].Bodengruppe", ""),
        };
        foreach (var (key, value) in overrides)
        {
            fields.RemoveAll(f => f.Key == key);
            fields.Add(new(key, value));
        }

        return fields;
    }

    private static Task<HttpResponseMessage> PostAsync(HttpClient client, int id, IEnumerable<KeyValuePair<string, string>> fields, string? token)
    {
        var all = fields.ToList();
        if (token is not null) { all.Add(new("__RequestVerificationToken", token)); }
        return client.PostAsync($"/Projekte/{id}/Feld", new FormUrlEncodedContent(all));
    }

    private async Task<int> CountAsync(int projectId)
    {
        int n = 0;
        await _factory.WithDbAsync(async db => n = await db.Aufschluesse.CountAsync(a => a.ProjectId == projectId));
        return n;
    }

    // ---------- GET ----------

    [Fact]
    public async Task Get_ReturnsOk_WithTitle_MarkedTab_AndBothSections()
    {
        int id = await SeedAsync();
        var response = await NewClient().GetAsync($"/Projekte/{id}/Feld");
        var html = Decode(await response.Content.ReadAsStringAsync());

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Contains("<title>Feldaufnahme · BoreholeHub</title>", html);
        Assert.Contains($"<a class=\"tab\" href=\"/Projekte/{id}/Feld\" aria-current=\"page\">Feldaufnahme</a>", html);
        Assert.Contains("Kleinrammbohrung (KRB)", html);
        Assert.Contains("Rammsondierung (DPH)", html);
        Assert.Contains("id=\"section-krb\"", html);
        Assert.Contains("id=\"section-dph\"", html);
        Assert.Contains("Schichten", html);
        Assert.Contains("Schlagzahlen N10", html);
        Assert.Contains("Standort übernehmen", html);
        Assert.Contains("Schicht hinzufügen", html);
        Assert.Contains("Speichern", html);
        Assert.Contains("type=\"reset\"", html);
        Assert.Contains("Zurücksetzen", html);
        Assert.Contains("/css/feld.css", html);
        Assert.Contains("/js/feld.js", html);
    }

    [Fact]
    public async Task Get_UnknownProject_Is404_ForPageAndUtm()
    {
        var client = NewClient();
        Assert.Equal(HttpStatusCode.NotFound, (await client.GetAsync("/Projekte/99999/Feld")).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, (await client.GetAsync("/Projekte/99999/Feld?handler=Utm&lat=50.9&lon=9.2")).StatusCode);
    }

    [Fact]
    public async Task Post_UnknownProject_Is404()
    {
        int id = await SeedAsync();
        var client = NewClient();
        string token = await TokenAsync(client, id);

        var response = await PostAsync(client, 99999, Krb7(), token);

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
    }

    [Fact]
    public async Task Get_PrefillsNextFreeName_ForDemoProject()
    {
        int id = await SeedAsync();
        var html = Decode(await NewClient().GetStringAsync($"/Projekte/{id}/Feld"));

        Assert.Matches("id=\"bezeichnung\"[^>]*value=\"KRB 7\"", html);
        Assert.Contains("data-prefill-krb=\"KRB 7\"", html);
        Assert.Contains("data-prefill-dph=\"DPH 4\"", html);
    }

    [Fact]
    public async Task Get_RendersThreeEmptyLayerRows_AndFortyN10Steps_AndHomogenbereichList()
    {
        int id = await SeedAsync();
        var html = Decode(await NewClient().GetStringAsync($"/Projekte/{id}/Feld"));

        Assert.Equal(3, Regex.Matches(html, "data-row id=\"schicht-\\d+\"").Count);
        Assert.Equal(40, Regex.Matches(html, "name=\"N10\\[\\d+\\]\"").Count);
        Assert.Contains("0–1 m", html);
        Assert.Contains("3–4 m", html);
        Assert.Contains("<datalist id=\"homogenbereiche\">", html);
        Assert.Contains("<option value=\"B3\">", html);
    }

    // ---------- Utm handler ----------

    [Fact]
    public async Task Utm_ReturnsRechtswertHochwert_Within2MetresOfDemoPlotCentre()
    {
        int id = await SeedAsync();
        var response = await NewClient().GetAsync($"/Projekte/{id}/Feld?handler=Utm&lat=50.91701&lon=9.23174");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        using var doc = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        double r = doc.RootElement.GetProperty("rechtswert").GetDouble();
        double h = doc.RootElement.GetProperty("hochwert").GetDouble();
        Assert.InRange(r, 516290 - 2, 516290 + 2);
        Assert.InRange(h, 5640622 - 2, 5640622 + 2);
        Assert.Equal(Math.Round(r, 1), r);
        Assert.Equal(Math.Round(h, 1), h);
    }

    [Theory]
    [InlineData("lat=abc&lon=9.2")]
    [InlineData("lat=50.9")]
    [InlineData("lon=9.2")]
    [InlineData("lat=50,9&lon=9,2")]
    [InlineData("lat=95&lon=9.2")]
    public async Task Utm_InvalidOrMissingValues_Return400(string query)
    {
        int id = await SeedAsync();
        var response = await NewClient().GetAsync($"/Projekte/{id}/Feld?handler=Utm&{query}");

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    // ---------- POST: save flow ----------

    [Fact]
    public async Task Post_ValidKrb_Redirects_SavesTwoSchichten_AndShowsSuccessOnce()
    {
        int id = await NewProjectAsync();
        var client = NewClient();
        string token = await TokenAsync(client, id);

        var response = await PostAsync(client, id, Krb7(), token);

        Assert.Equal(HttpStatusCode.Redirect, response.StatusCode);
        Assert.EndsWith($"/Projekte/{id}/Feld", response.Headers.Location!.ToString());

        Aufschluss saved = null!;
        await _factory.WithDbAsync(async db => saved = await db.Aufschluesse.Include(a => a.Schichten).SingleAsync(a => a.ProjectId == id));
        Assert.Equal("KRB 7", saved.Bezeichnung);
        Assert.Equal(AufschlussArt.Kleinrammbohrung, saved.Art);
        Assert.Equal(516296.0, saved.Rechtswert);
        Assert.Equal(212.8, saved.AnsatzhoeheMNhn);
        Assert.Equal(new DateOnly(2026, 11, 3), saved.Datum);
        Assert.Equal(2, saved.Schichten.Count);
        Assert.Equal(1e-6, saved.Schichten.OrderBy(s => s.VonM).First().KfWertMs);

        var html = Decode(await client.GetStringAsync($"/Projekte/{id}/Feld"));
        Assert.Contains("KRB 7 gespeichert.", html);
        Assert.Contains("Prüfung: keine Befunde.", html);
        Assert.Contains($"href=\"/Aufschluesse/{saved.Id}\"", html);
        Assert.Contains("Profil öffnen", html);
        Assert.Contains($"href=\"/Projekte/{id}/Pruefung\"", html);

        // The success box is shown once only.
        var again = Decode(await client.GetStringAsync($"/Projekte/{id}/Feld"));
        Assert.DoesNotContain("KRB 7 gespeichert.", again);
    }

    [Fact]
    public async Task Post_KrbWithGuLayerWithoutKf_Saves_AndShowsVersickerungHinweis()
    {
        int id = await NewProjectAsync();
        var client = NewClient();
        string token = await TokenAsync(client, id);
        var fields = Krb7(
            ("Schichten[0].Kf", "1e-6"),
            ("Schichten[1].Kurzzeichen", "G, s, u'"), ("Schichten[1].Bodengruppe", "GU"), ("Schichten[1].Kf", ""));

        var response = await PostAsync(client, id, fields, token);

        Assert.Equal(HttpStatusCode.Redirect, response.StatusCode);
        Assert.Equal(1, await CountAsync(id));
        var html = Decode(await client.GetStringAsync($"/Projekte/{id}/Feld"));
        Assert.Contains("KRB 7 gespeichert.", html);
        Assert.Contains("Prüfung: 1 Hinweis.", html);
        Assert.Contains("kf-Wert fehlt für Schicht 0,40–3,00 m (GU) – Versickerung nicht prüfbar.", html);
        Assert.Contains("Profil öffnen", html);
        Assert.Contains("Zur Prüfung", html);
    }

    [Fact]
    public async Task Post_KfInDifferentNotations_AreAllParsed()
    {
        int id = await NewProjectAsync();
        var client = NewClient();
        string token = await TokenAsync(client, id);
        var fields = Krb7(
            ("Schichten[0].Bis", "1,00"), ("Schichten[0].Kf", "2e-5"), ("Schichten[0].Bodengruppe", "GU"),
            ("Schichten[1].Von", "1,00"), ("Schichten[1].Bis", "2,00"), ("Schichten[1].Bodengruppe", "GW"), ("Schichten[1].Kf", "2E-05"),
            ("Schichten[2].Von", "2,00"), ("Schichten[2].Bis", "3,00"), ("Schichten[2].Kurzzeichen", "G, s"), ("Schichten[2].Bodengruppe", "GU"), ("Schichten[2].Kf", "0,00002"));

        var response = await PostAsync(client, id, fields, token);

        Assert.Equal(HttpStatusCode.Redirect, response.StatusCode);
        Aufschluss saved = null!;
        await _factory.WithDbAsync(async db => saved = await db.Aufschluesse.Include(a => a.Schichten).SingleAsync(a => a.ProjectId == id));
        Assert.Equal(3, saved.Schichten.Count);
        Assert.All(saved.Schichten, s => Assert.Equal(2e-5, s.KfWertMs!.Value, 12));
    }

    [Fact]
    public async Task Post_KrbWithLayerGap_Returns200_WithLuecke_AndSavesNothing()
    {
        int id = await NewProjectAsync();
        var client = NewClient();
        string token = await TokenAsync(client, id);
        var fields = Krb7(("Schichten[1].Von", "0,80"));

        var response = await PostAsync(client, id, fields, token);
        var html = Decode(await response.Content.ReadAsStringAsync());

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Contains("Lücke 0,40–0,80 m.", html);
        Assert.Contains("Nichts gespeichert", html);
        Assert.Equal(0, await CountAsync(id));
        // All entered values are kept.
        Assert.Matches("id=\"bezeichnung\"[^>]*value=\"KRB 7\"", html);
        Assert.Contains("value=\"516296,0\"", html);
        Assert.Contains("value=\"0,80\"", html);
    }

    [Fact]
    public async Task Post_KrbWithoutLayers_IsBlockedByDataQuality()
    {
        int id = await NewProjectAsync();
        var client = NewClient();
        string token = await TokenAsync(client, id);
        var fields = Krb7().Where(f => !f.Key.StartsWith("Schichten[")).ToList();

        var response = await PostAsync(client, id, fields, token);
        var html = Decode(await response.Content.ReadAsStringAsync());

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Contains("Keine Schichten erfasst.", html);
        Assert.Equal(0, await CountAsync(id));
    }

    [Fact]
    public async Task Post_Dph_With30Values_SavesThirtyMesswerte()
    {
        int id = await NewProjectAsync();
        var client = NewClient();
        string token = await TokenAsync(client, id);
        var fields = new List<KeyValuePair<string, string>>
        {
            new("Art", "DPH"), new("Bezeichnung", "DPH 1"), new("Datum", "2026-11-03"),
            new("Rechtswert", "516300,0"), new("Hochwert", "5640628,0"), new("Ansatzhoehe", "212,7"), new("Endteufe", "3,0"),
        };
        for (int i = 1; i <= 30; i++) { fields.Add(new($"N10[{i}]", (2 + i % 5).ToString())); }
        fields.Add(new("N10[31]", ""));

        var response = await PostAsync(client, id, fields, token);

        Assert.Equal(HttpStatusCode.Redirect, response.StatusCode);
        Aufschluss saved = null!;
        await _factory.WithDbAsync(async db => saved = await db.Aufschluesse.Include(a => a.Messwerte).SingleAsync(a => a.ProjectId == id));
        Assert.Equal(AufschlussArt.Rammsondierung, saved.Art);
        Assert.Equal(30, saved.Messwerte.Count);
        Assert.Equal(0.1, saved.Messwerte.Min(m => m.TiefeBisM), 6);
        Assert.Equal(3.0, saved.Messwerte.Max(m => m.TiefeBisM), 6);
        var html = Decode(await client.GetStringAsync($"/Projekte/{id}/Feld"));
        Assert.Contains("DPH 1 gespeichert.", html);
        Assert.Contains("Prüfung: keine Befunde.", html);
    }

    [Fact]
    public async Task Post_InvalidN10_Returns200_AndSavesNothing()
    {
        int id = await NewProjectAsync();
        var client = NewClient();
        string token = await TokenAsync(client, id);
        var fields = new List<KeyValuePair<string, string>>
        {
            new("Art", "DPH"), new("Bezeichnung", "DPH 1"), new("Datum", "2026-11-03"),
            new("Rechtswert", "516300,0"), new("Hochwert", "5640628,0"), new("Ansatzhoehe", "212,7"), new("Endteufe", "0,5"),
            new("N10[1]", "3"), new("N10[2]", "-4"), new("N10[3]", "x"),
        };

        var response = await PostAsync(client, id, fields, token);
        var html = Decode(await response.Content.ReadAsStringAsync());

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Contains("N10 bei 0,20 m: Bitte eine ganze Zahl ab 0 eingeben.", html);
        Assert.Contains("N10 bei 0,30 m: Bitte eine ganze Zahl ab 0 eingeben.", html);
        Assert.Equal(0, await CountAsync(id));
    }

    // ---------- POST: validation ----------

    [Fact]
    public async Task Post_DuplicateBezeichnung_Returns200_WithMessage_AndSavesNothing()
    {
        int id = await SeedAsync();
        int before = await CountAsync(id);
        var client = NewClient();
        string token = await TokenAsync(client, id);

        var response = await PostAsync(client, id, Krb7(("Bezeichnung", "  krb 1 ")), token);
        var html = Decode(await response.Content.ReadAsStringAsync());

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Contains("Bezeichnung „krb 1“ ist in diesem Projekt bereits vergeben.", html);
        Assert.Equal(before, await CountAsync(id));
    }

    [Fact]
    public async Task Post_NegativeGrundwasser_Returns200_WithMessage_AndSavesNothing()
    {
        int id = await NewProjectAsync();
        var client = NewClient();
        string token = await TokenAsync(client, id);

        var response = await PostAsync(client, id, Krb7(("Grundwasser", "-5")), token);
        var html = Decode(await response.Content.ReadAsStringAsync());

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Contains("Grundwasser: Der Wert darf nicht negativ sein.", html);
        Assert.Equal(0, await CountAsync(id));
    }

    [Fact]
    public async Task Post_GrundwasserBelowEndteufe_Returns200_WithMessage_AndSavesNothing()
    {
        int id = await NewProjectAsync();
        var client = NewClient();
        string token = await TokenAsync(client, id);

        var response = await PostAsync(client, id, Krb7(("Endteufe", "3,0"), ("Grundwasser", "99")), token);
        var html = Decode(await response.Content.ReadAsStringAsync());

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Contains("Grundwasser: Der Wert liegt tiefer als die Endteufe.", html);
        Assert.Equal(0, await CountAsync(id));
    }

    [Fact]
    public async Task Post_ValidGrundwasser_StillSaves()
    {
        int id = await NewProjectAsync();
        var client = NewClient();
        string token = await TokenAsync(client, id);

        var response = await PostAsync(client, id, Krb7(("Grundwasser", "1,50")), token);

        Assert.Equal(HttpStatusCode.Redirect, response.StatusCode);
        Assert.Equal(1, await CountAsync(id));
    }

    [Fact]
    public async Task Post_DatumBefore2000_Returns200_WithMessage_AndSavesNothing()
    {
        int id = await NewProjectAsync();
        var client = NewClient();
        string token = await TokenAsync(client, id);

        var response = await PostAsync(client, id, Krb7(("Datum", "1999-12-31")), token);
        var html = Decode(await response.Content.ReadAsStringAsync());

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Contains($"Datum: Bitte ein Datum zwischen 01.01.2000 und {BoreholeHub.Web.Import.CsvImportService.MaxDatum():dd.MM.yyyy} eingeben.", html);
        Assert.Equal(0, await CountAsync(id));
    }

    [Fact]
    public async Task Post_DatumOneDayAfterMax_Returns200_WithMessage_AndSavesNothing()
    {
        int id = await NewProjectAsync();
        var client = NewClient();
        string token = await TokenAsync(client, id);
        var tooLate = BoreholeHub.Web.Import.CsvImportService.MaxDatum().AddDays(1);

        var response = await PostAsync(client, id, Krb7(("Datum", tooLate.ToString("yyyy-MM-dd"))), token);
        var html = Decode(await response.Content.ReadAsStringAsync());

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Contains("Datum: Bitte ein Datum zwischen 01.01.2000 und", html);
        Assert.Equal(0, await CountAsync(id));
    }

    [Fact]
    public async Task Post_LayerErrorAfterEmptyFirstRow_IsNumberedAndLinkedByPosition()
    {
        int id = await NewProjectAsync();
        var client = NewClient();
        string token = await TokenAsync(client, id);
        var fields = Krb7(
            ("Schichten[0].Von", ""), ("Schichten[0].Bis", ""), ("Schichten[0].Kurzzeichen", ""),
            ("Schichten[0].Beschreibung", ""), ("Schichten[0].Bodengruppe", ""), ("Schichten[0].Kf", ""),
            ("Schichten[1].Von", "x"));

        var response = await PostAsync(client, id, fields, token);
        var html = Decode(await response.Content.ReadAsStringAsync());

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Contains("Schicht 1, Von: Bitte eine Zahl eingeben.", html);
        Assert.Contains("href=\"#schicht-0\"", html);
    }

    [Fact]
    public async Task Post_NonNumericEndteufe_Returns200_WithNumberMessage()
    {
        int id = await NewProjectAsync();
        var client = NewClient();
        string token = await TokenAsync(client, id);

        var response = await PostAsync(client, id, Krb7(("Endteufe", "drei")), token);
        var html = Decode(await response.Content.ReadAsStringAsync());

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Contains("Endteufe: Bitte eine Zahl eingeben.", html);
        Assert.Contains("value=\"drei\"", html);
        Assert.Equal(0, await CountAsync(id));
    }

    [Fact]
    public async Task Post_LayerRowWithValueButWithoutBounds_IsRejected()
    {
        int id = await NewProjectAsync();
        var client = NewClient();
        string token = await TokenAsync(client, id);

        var response = await PostAsync(client, id, Krb7(("Schichten[2].Farbe", "grau")), token);
        var html = Decode(await response.Content.ReadAsStringAsync());

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Contains("Schicht 3, Von: Bitte eine Zahl eingeben.", html);
        Assert.Contains("Schicht 3: Bodengruppe oder Kurzzeichen angeben.", html);
        Assert.Equal(0, await CountAsync(id));
    }

    [Fact]
    public async Task Post_WithoutAntiforgeryToken_Returns400()
    {
        int id = await NewProjectAsync();
        var client = NewClient();
        await TokenAsync(client, id);   // sets the antiforgery cookie, but the token is not sent

        var response = await PostAsync(client, id, Krb7(), token: null);

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        Assert.Equal(0, await CountAsync(id));
    }

    // ---------- tabs ----------

    [Theory]
    [InlineData("")]
    [InlineData("/Karte")]
    [InlineData("/Pruefung")]
    [InlineData("/Daten")]
    [InlineData("/Feld")]
    public async Task Tab_Feldaufnahme_IsTheLastTab_OnProjectPages(string suffix)
    {
        int id = await SeedAsync();
        var html = Decode(await NewClient().GetStringAsync($"/Projekte/{id}{suffix}"));

        var tabs = Regex.Matches(html, "<a class=\"tab\" href=\"([^\"]+)\"[^>]*>([^<]+)</a>");
        Assert.Equal(7, tabs.Count);
        Assert.Equal("Feldaufnahme", tabs[^1].Groups[2].Value);
        Assert.Equal($"/Projekte/{id}/Feld", tabs[^1].Groups[1].Value);
        Assert.Equal("Daten", tabs[^2].Groups[2].Value);
    }
}
