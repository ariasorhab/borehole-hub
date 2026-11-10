using System.Globalization;
using System.Net;
using System.Text;
using System.Text.RegularExpressions;
using BoreholeHub.Core.Entities;
using BoreholeHub.Web.Data;
using BoreholeHub.Web.Import;
using Microsoft.EntityFrameworkCore;

namespace BoreholeHub.Tests;

/// <summary>DE/EN checks for the Daten page and the CSV import messages (T46, D128-style templates).</summary>
public class LocalizationDatenTests : IClassFixture<WebAppFactory>
{
    private static readonly CultureInfo De = CultureInfo.GetCultureInfo("de-DE");

    private const string Kopf = "Bezeichnung;Art;Rechtswert;Hochwert;Ansatzhoehe_mNHN;Endteufe_m;Datum;Grundwasser_m\r\n";
    private const string Aufschluesse =
        Kopf +
        "KRB 6;KRB;516296,5;5640626;212,8;3,0;27.10.2026;\r\n" +
        "DPH 4;DPH;516300;5640628;212,7;4,0;28.10.2026;2,9\r\n";

    private const string SchichtenKopf =
        "Bezeichnung;Von_m;Bis_m;Kurzzeichen;Beschreibung;Bodengruppe;Farbe;Zustand;Homogenbereich;Frostklasse;kf_m_s\r\n";

    /// <summary>Layer 3 leaves a gap between 1,20 m and 1,40 m.</summary>
    private const string SchichtenMitLuecke =
        SchichtenKopf +
        "KRB 6;0,00;1,20;U, t;Auenlehm;TL;braun;steif;B2;F3;5E-08\r\n" +
        "KRB 6;1,40;3,00;G, s;Flusskies;GW;grau;mitteldicht;B3;F1;\r\n";

    /// <summary>Two layers that cover 0 to 3,00 m: a valid import.</summary>
    private const string SchichtenGueltig =
        SchichtenKopf +
        "KRB 6;0,00;1,20;U, t;Auenlehm;TL;braun;steif;B2;F3;5E-08\r\n" +
        "KRB 6;1,20;3,00;G, s;Flusskies;GW;grau;mitteldicht;B3;F1;2,0E-03\r\n";

    private readonly WebAppFactory _factory;

    public LocalizationDatenTests(WebAppFactory factory) => _factory = factory;

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

    /// <summary>Posts the two CSV files like the page does: antiforgery token from the page, then the multipart form.</summary>
    private static async Task<HttpResponseMessage> PostImportAsync(HttpClient client, int id, string aufschluesse, string schichten)
    {
        var page = await client.GetStringAsync($"/Projekte/{id}/Daten");
        var token = Regex.Match(page, "name=\"__RequestVerificationToken\"[^>]*value=\"([^\"]+)\"").Groups[1].Value;
        Assert.NotEmpty(token);
        var form = new MultipartFormDataContent { { new StringContent(token), "__RequestVerificationToken" } };
        form.Add(new ByteArrayContent(Encoding.UTF8.GetBytes(aufschluesse)), "aufschluesseCsv", "a.csv");
        form.Add(new ByteArrayContent(Encoding.UTF8.GetBytes(schichten)), "schichtenCsv", "s.csv");
        return await client.PostAsync($"/Projekte/{id}/Daten?handler=Import", form);
    }

    [Fact]
    public async Task Daten_English_ShowsEnglishPage_AndNoGermanHeadings()
    {
        int id = await NewProjectAsync("Daten englisch");
        var client = await LanguageTestHelper.CreateEnglishClientAsync(_factory);

        var html = WebUtility.HtmlDecode(await client.GetStringAsync($"/Projekte/{id}/Daten"));

        Assert.Contains("<html lang=\"en\"", html);
        Assert.Contains("<h1>Data</h1>", html);
        Assert.Contains("GeoJSON with all exploration points of this project (WGS84).", html);
        Assert.Contains("Download GeoJSON", html);
        Assert.Contains("Add exploration points and layers from CSV files to the project.", html);
        Assert.Contains("<h3>Rules</h3>", html);
        Assert.DoesNotContain("<h1>Daten</h1>", html);
        Assert.DoesNotContain("Regeln</h3>", html);
        Assert.DoesNotContain("Aufschlüsse und Schichten aus CSV-Dateien", html);
    }

    [Fact]
    public async Task Import_English_WithError_ShowsEnglishErrorAfterPost()
    {
        int id = await NewProjectAsync("Import englisch Fehler");
        var client = await LanguageTestHelper.CreateEnglishClientAsync(_factory);

        var response = await PostImportAsync(client, id, Aufschluesse, SchichtenMitLuecke);
        var html = WebUtility.HtmlDecode(await response.Content.ReadAsStringAsync());

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Contains("Layers, line 3: gap between 1,20 m and 1,40 m at KRB 6", html);
        Assert.Contains("Nothing has been saved.", html);
        Assert.Contains("<h2 id=\"ergebnis-titel\">Import not possible (", html);
        Assert.DoesNotContain("Es wurde nichts gespeichert.", html);
        await _factory.WithDbAsync(async db => Assert.Equal(0, await db.Aufschluesse.CountAsync(a => a.ProjectId == id)));
    }

    [Fact]
    public async Task Import_English_Success_RedirectsAndShowsCountsInWords()
    {
        int id = await NewProjectAsync("Import englisch OK");
        var client = await LanguageTestHelper.CreateEnglishClientAsync(_factory);

        var response = await PostImportAsync(client, id, Aufschluesse, SchichtenGueltig);
        Assert.Equal(HttpStatusCode.Redirect, response.StatusCode);

        var follow = await client.GetAsync($"/Projekte/{id}/Daten");
        var html = WebUtility.HtmlDecode(await follow.Content.ReadAsStringAsync());

        Assert.Contains("<h2 id=\"ergebnis-titel\">Import complete</h2>", html);
        Assert.Contains("2 exploration points and 2 layers imported.", html);
        Assert.Matches(@"Data check: (1 error|\d+ errors), (1 notice|\d+ notices)\.", html);
    }

    [Fact]
    public void ImportErrors_AreTemplatesWhoseFormattedTextIsTheGermanError()
    {
        var r = new CsvImportService().Parse(Aufschluesse, SchichtenMitLuecke, []);

        Assert.NotEmpty(r.Fehler);
        Assert.Equal(r.Fehler.Count, r.FehlerTexte.Count);
        for (int i = 0; i < r.Fehler.Count; i++)
        {
            Assert.Equal(r.Fehler[i], string.Format(De, r.FehlerTexte[i].Vorlage, r.FehlerTexte[i].Argumente.ToArray<object>()));
        }
        Assert.Contains("Schichten, Zeile 3: Lücke zwischen 1,20 m und 1,40 m bei KRB 6", r.Fehler);
    }

    [Fact]
    public async Task Daten_German_StaysGerman_WithoutCookie()
    {
        int id = await NewProjectAsync("Daten deutsch");
        var client = _factory.CreateClient();

        var html = WebUtility.HtmlDecode(await client.GetStringAsync($"/Projekte/{id}/Daten"));

        Assert.Contains("<html lang=\"de\"", html);
        Assert.Contains("<h1>Daten</h1>", html);
        Assert.Contains("Öffnet direkt in QGIS (Ebene hinzufügen › Vektor)", html);
        Assert.Contains("<h3>Regeln</h3>", html);
        Assert.DoesNotContain("<h3>Rules</h3>", html);
    }
}
