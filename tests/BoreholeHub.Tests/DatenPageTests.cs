using System.Net;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;
using BoreholeHub.Web.Data;
using Microsoft.EntityFrameworkCore;

namespace BoreholeHub.Tests;

public class DatenPageTests : IClassFixture<WebAppFactory>
{
    private readonly WebAppFactory _factory;

    public DatenPageTests(WebAppFactory factory) => _factory = factory;

    private async Task<int> SeedAsync()
    {
        int id = 0;
        await _factory.WithDbAsync(async db => id = await DemoDataSeeder.SeedAsync(db));
        return id;
    }

    private async Task<int> NewProjectAsync(string name)
    {
        int id = 0;
        await _factory.WithDbAsync(async db =>
        {
            var p = new BoreholeHub.Core.Entities.Project { Name = name, Ort = "Test", Auftraggeber = "Test", ErstelltAm = new DateTime(2026, 11, 2) };
            db.Projects.Add(p);
            await db.SaveChangesAsync();
            id = p.Id;
        });
        return id;
    }

    private async Task<HttpResponseMessage> PostImportAsync(HttpClient client, int id, string? aufschluesse, string? schichten)
    {
        var page = await client.GetStringAsync($"/Projekte/{id}/Daten");
        var token = Regex.Match(page, "name=\"__RequestVerificationToken\"[^>]*value=\"([^\"]+)\"").Groups[1].Value;
        Assert.NotEmpty(token);
        var form = new MultipartFormDataContent { { new StringContent(token), "__RequestVerificationToken" } };
        if (aufschluesse is not null) { form.Add(new ByteArrayContent(Encoding.UTF8.GetBytes(aufschluesse)), "aufschluesseCsv", "a.csv"); }
        if (schichten is not null) { form.Add(new ByteArrayContent(Encoding.UTF8.GetBytes(schichten)), "schichtenCsv", "s.csv"); }
        return await client.PostAsync($"/Projekte/{id}/Daten?handler=Import", form);
    }

    private const string A2 =
        "Bezeichnung;Art;Rechtswert;Hochwert;Ansatzhoehe_mNHN;Endteufe_m;Datum;Grundwasser_m\r\n" +
        "KRB 6;KRB;516296,5;5640626;212,8;3,0;27.10.2026;\r\n" +
        "DPH 4;DPH;516300;5640628;212,7;4,0;28.10.2026;2,9\r\n";

    [Fact]
    public async Task Daten_ReturnsOk_WithTabs_AndUnknownIdIs404()
    {
        int id = await SeedAsync();
        var client = _factory.CreateClient();
        var response = await client.GetAsync($"/Projekte/{id}/Daten");
        var html = WebUtility.HtmlDecode(await response.Content.ReadAsStringAsync());

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Contains("aria-current=\"page\">Daten</a>", html);
        Assert.Contains("Öffnet direkt in QGIS (Ebene hinzufügen › Vektor)", html);
        Assert.Contains("name=\"aufschluesseCsv\"", html);
        Assert.Contains("name=\"schichtenCsv\"", html);
        Assert.Equal(HttpStatusCode.NotFound, (await client.GetAsync("/Projekte/99999/Daten")).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, (await client.GetAsync("/Projekte/99999/Daten?handler=GeoJson")).StatusCode);
    }

    [Theory]
    [InlineData("VorlageAufschluesse", "vorlage-aufschluesse.csv", "Bezeichnung;Art;Rechtswert;Hochwert;Ansatzhoehe_mNHN;Endteufe_m;Datum;Grundwasser_m")]
    [InlineData("VorlageSchichten", "vorlage-schichten.csv", "Bezeichnung;Von_m;Bis_m;Kurzzeichen;Beschreibung;Bodengruppe;Farbe;Zustand;Homogenbereich;Frostklasse;kf_m_s")]
    public async Task Templates_HaveExactHeader_Bom_AndTwoExampleRows(string handler, string fileName, string header)
    {
        int id = await SeedAsync();
        var response = await _factory.CreateClient().GetAsync($"/Projekte/{id}/Daten?handler={handler}");
        var bytes = await response.Content.ReadAsByteArrayAsync();

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Equal(fileName, response.Content.Headers.ContentDisposition!.FileName!.Trim('"'));
        Assert.Equal([0xEF, 0xBB, 0xBF], bytes.Take(3).ToArray());
        var lines = Encoding.UTF8.GetString(bytes).TrimStart('﻿').Split("\r\n", StringSplitOptions.RemoveEmptyEntries);
        Assert.Equal(header, lines[0]);
        Assert.Equal(3, lines.Length);
    }

    [Fact]
    public async Task TemplatesTogether_AreValidInput_ForTheImporter()
    {
        int id = await NewProjectAsync("Vorlagen-Test");
        var client = _factory.CreateClient();
        string a = Encoding.UTF8.GetString(await client.GetByteArrayAsync($"/Projekte/{id}/Daten?handler=VorlageAufschluesse"));
        string s = Encoding.UTF8.GetString(await client.GetByteArrayAsync($"/Projekte/{id}/Daten?handler=VorlageSchichten"));
        var r = new BoreholeHub.Web.Import.CsvImportService().Parse(BoreholeHub.Web.Import.CsvImportService.Decode(Encoding.UTF8.GetBytes(a)), BoreholeHub.Web.Import.CsvImportService.Decode(Encoding.UTF8.GetBytes(s)), []);

        Assert.Empty(r.Fehler);
        Assert.Equal(2, r.NeueAufschluesse.Count);
        Assert.Equal(2, r.NeueSchichten.Count);
    }

    [Fact]
    public async Task GeoJson_HasEightFeatures_WithExtraProperties_AndSchwalmstadtCoordinates()
    {
        int id = await SeedAsync();
        var response = await _factory.CreateClient().GetAsync($"/Projekte/{id}/Daten?handler=GeoJson");
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Equal("neubau-einfamilienhaus-mit-garage-schwalmstadt-ziegenhain-demo-aufschluesse.geojson",
            response.Content.Headers.ContentDisposition!.FileName!.Trim('"'));

        using var doc = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        var root = doc.RootElement;
        Assert.Equal("FeatureCollection", root.GetProperty("type").GetString());
        var features = root.GetProperty("features");
        Assert.Equal(8, features.GetArrayLength());
        foreach (var f in features.EnumerateArray())
        {
            var c = f.GetProperty("geometry").GetProperty("coordinates");
            Assert.InRange(c[0].GetDouble(), 9.231739 - 0.002, 9.231739 + 0.002);
            Assert.InRange(c[1].GetDouble(), 50.917012 - 0.002, 50.917012 + 0.002);
            var p = f.GetProperty("properties");
            foreach (var name in new[] { "id", "bezeichnung", "art", "endteufeM", "grundwasserM", "ansatzhoeheMNhn", "rechtswert", "hochwert", "projekt", "datum", "schichten" })
            {
                Assert.True(p.TryGetProperty(name, out _), name);
            }

            Assert.Matches("^\\d{4}-\\d{2}-\\d{2}$", p.GetProperty("datum").GetString()!);
            Assert.Equal(DemoDataSeeder.DemoProjectName, p.GetProperty("projekt").GetString());
        }

        var krb1 = features.EnumerateArray().First(f => f.GetProperty("properties").GetProperty("bezeichnung").GetString() == "KRB 1");
        Assert.Equal(4, krb1.GetProperty("properties").GetProperty("schichten").GetInt32());
    }

    [Fact]
    public async Task Import_Valid_SavesAndShowsSuccessMessage()
    {
        int id = await NewProjectAsync("Import-Test OK");
        var response = await PostImportAsync(_factory.CreateClient(), id, A2,
            "Bezeichnung;Von_m;Bis_m;Kurzzeichen;Beschreibung;Bodengruppe;Farbe;Zustand;Homogenbereich;Frostklasse;kf_m_s\r\n" +
            "KRB 6;0,00;1,20;U, t;Auenlehm;TL;braun;steif;B2;F3;5E-08\r\nKRB 6;1,20;3,00;G, s;Flusskies;GW;grau;mitteldicht;B3;F1;2,0E-03\r\n");
        var html = WebWebUtility(await response.Content.ReadAsStringAsync());

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Contains("2 Aufschlüsse und 2 Schichten importiert.", html);
        Assert.Contains($"href=\"/Projekte/{id}/Karte\"", html);
        await _factory.WithDbAsync(async db =>
        {
            var list = await db.Aufschluesse.Include(a => a.Schichten).Where(a => a.ProjectId == id).ToListAsync();
            Assert.Equal(2, list.Count);
            Assert.Equal(2, list.Single(a => a.Bezeichnung == "KRB 6").Schichten.Count);
        });
    }

    [Fact]
    public async Task Import_WithError_SavesNothing_AndListsErrors()
    {
        int id = await NewProjectAsync("Import-Test Fehler");
        var response = await PostImportAsync(_factory.CreateClient(), id, A2,
            "Bezeichnung;Von_m;Bis_m;Kurzzeichen;Beschreibung;Bodengruppe;Farbe;Zustand;Homogenbereich;Frostklasse;kf_m_s\r\n" +
            "KRB 6;0,00;1,20;U, t;Auenlehm;TL;braun;steif;B2;F3;5E-08\r\nKRB 6;1,40;3,00;G, s;Flusskies;GW;grau;mitteldicht;B3;F1;\r\n");
        var html = WebWebUtility(await response.Content.ReadAsStringAsync());

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Contains("Schichten, Zeile 3: Lücke zwischen 1,20 m und 1,40 m bei KRB 6", html);
        Assert.Contains("Es wurde nichts gespeichert.", html);
        await _factory.WithDbAsync(async db => Assert.Equal(0, await db.Aufschluesse.CountAsync(a => a.ProjectId == id)));
    }

    [Fact]
    public async Task Import_WithoutAntiforgeryToken_IsRejected()
    {
        int id = await NewProjectAsync("Import-Test CSRF");
        var form = new MultipartFormDataContent { { new ByteArrayContent(Encoding.UTF8.GetBytes(A2)), "aufschluesseCsv", "a.csv" } };
        var response = await _factory.CreateClient().PostAsync($"/Projekte/{id}/Daten?handler=Import", form);

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    [Fact]
    public async Task Import_FileLargerThanOneMb_IsRejectedWithMessage()
    {
        int id = await NewProjectAsync("Import-Test Gross");
        string big = A2 + new string('x', 1_100_000);
        var response = await PostImportAsync(_factory.CreateClient(), id, big, null);
        var html = WebWebUtility(await response.Content.ReadAsStringAsync());

        Assert.Contains("größer als 1 MB", html);
        await _factory.WithDbAsync(async db => Assert.Equal(0, await db.Aufschluesse.CountAsync(a => a.ProjectId == id)));
    }

    private static string WebWebUtility(string html) => WebUtility.HtmlDecode(html);
}
