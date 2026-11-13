using System.Net;
using System.Text;
using System.Text.RegularExpressions;
using BoreholeHub.Core.Entities;
using BoreholeHub.Web.Data;
using BoreholeHub.Web.Pages.Projekte;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.EntityFrameworkCore;

namespace BoreholeHub.Tests;

/// <summary>Hosting limit (D166): at most 200 Aufschlüsse per project, for the import and for the field form.</summary>
public class HostingLimitTests : IClassFixture<WebAppFactory>
{
    private readonly WebAppFactory _factory;

    public HostingLimitTests(WebAppFactory factory) => _factory = factory;

    private HttpClient NewClient() => _factory.CreateClient(new WebApplicationFactoryClientOptions { AllowAutoRedirect = false });

    private async Task<int> NewProjectAsync()
    {
        int id = 0;
        await _factory.WithDbAsync(async db =>
        {
            var p = new Project { Name = "Limit-Test " + Guid.NewGuid().ToString("N")[..6], Ort = "Test", Auftraggeber = "Test", ErstelltAm = new DateTime(2026, 11, 2) };
            db.Projects.Add(p);
            await db.SaveChangesAsync();
            id = p.Id;
        });
        return id;
    }

    private async Task<int> CountAsync(int projectId)
    {
        int n = 0;
        await _factory.WithDbAsync(async db => n = await db.Aufschluesse.CountAsync(a => a.ProjectId == projectId));
        return n;
    }

    private static string Decode(string html) => System.Net.WebUtility.HtmlDecode(html);

    // ---------- import ----------

    private const string SchichtenKopf = "Bezeichnung;Von_m;Bis_m;Kurzzeichen;Beschreibung;Bodengruppe;Farbe;Zustand;Homogenbereich;Frostklasse;kf_m_s\r\n";
    private const string AufschluesseKopf = "Bezeichnung;Art;Rechtswert;Hochwert;Ansatzhoehe_mNHN;Endteufe_m;Datum;Grundwasser_m\r\n";

    /// <summary>One KRB per number from <paramref name="von"/> to <paramref name="bis"/>, each with one layer to 3,00 m.</summary>
    private static (string Aufschluesse, string Schichten) Krbs(int von, int bis)
    {
        var a = new StringBuilder(AufschluesseKopf);
        var s = new StringBuilder(SchichtenKopf);
        for (int i = von; i <= bis; i++)
        {
            a.Append($"KRB {i};KRB;516296,5;{5640626 + i};212,8;3,0;27.10.2026;\r\n");
            s.Append($"KRB {i};0,00;3,00;U, t;Auenlehm;TL;braun;steif;B2;F3;5E-08\r\n");
        }

        return (a.ToString(), s.ToString());
    }

    private async Task<HttpResponseMessage> PostImportAsync(int id, string aufschluesse, string schichten)
    {
        var client = NewClient();
        var page = await client.GetStringAsync($"/Projekte/{id}/Daten");
        var token = Regex.Match(page, "name=\"__RequestVerificationToken\"[^>]*value=\"([^\"]+)\"").Groups[1].Value;
        Assert.NotEmpty(token);
        var form = new MultipartFormDataContent { { new StringContent(token), "__RequestVerificationToken" } };
        form.Add(new ByteArrayContent(Encoding.UTF8.GetBytes(aufschluesse)), "aufschluesseCsv", "a.csv");
        form.Add(new ByteArrayContent(Encoding.UTF8.GetBytes(schichten)), "schichtenCsv", "s.csv");
        return await client.PostAsync($"/Projekte/{id}/Daten?handler=Import", form);
    }

    [Fact]
    public async Task Import_MoreThan200_IsRefusedAsAWhole_AndSavesNothing()
    {
        int id = await NewProjectAsync();
        var (a, s) = Krbs(1, 201);

        var response = await PostImportAsync(id, a, s);
        var html = Decode(await response.Content.ReadAsStringAsync());

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Contains("Das Projekt darf höchstens 200 Aufschlüsse enthalten (vorhanden: 0, neu: 201).", html);
        Assert.Contains("Es wurde nichts gespeichert.", html);
        Assert.Equal(0, await CountAsync(id));
    }

    [Fact]
    public async Task Import_CountsExistingAufschluesse_AndRefusesTheWholeImport()
    {
        int id = await NewProjectAsync();
        var (a1, s1) = Krbs(1, 150);
        var first = await PostImportAsync(id, a1, s1);
        Assert.Equal(HttpStatusCode.Redirect, first.StatusCode);
        Assert.Equal(150, await CountAsync(id));

        var (a2, s2) = Krbs(151, 201); // 51 new, 150 existing: 201 in total
        var response = await PostImportAsync(id, a2, s2);
        var html = Decode(await response.Content.ReadAsStringAsync());

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Contains("Das Projekt darf höchstens 200 Aufschlüsse enthalten (vorhanden: 150, neu: 51).", html);
        Assert.Equal(150, await CountAsync(id));
    }

    [Fact]
    public async Task Import_ExactlyReachingTheLimit_IsAccepted()
    {
        int id = await NewProjectAsync();
        var (a, s) = Krbs(1, 200);

        var response = await PostImportAsync(id, a, s);

        Assert.Equal(HttpStatusCode.Redirect, response.StatusCode);
        Assert.Equal(200, await CountAsync(id));
    }

    // ---------- field form ----------

    private async Task SeedAsync(int projectId, int count)
    {
        await _factory.WithDbAsync(async db =>
        {
            for (int i = 1; i <= count; i++)
            {
                db.Aufschluesse.Add(new Aufschluss
                {
                    ProjectId = projectId,
                    Bezeichnung = "Seed " + i,
                    Art = BoreholeHub.Core.Entities.AufschlussArt.Kleinrammbohrung,
                    Rechtswert = 516296 + i,
                    Hochwert = 5640626 + i,
                    AnsatzhoeheMNhn = 212.8,
                    EndteufeM = 3.0,
                    Datum = new DateOnly(2026, 10, 27),
                });
            }

            await db.SaveChangesAsync();
        });
    }

    private static List<KeyValuePair<string, string>> Krb7()
    {
        return
        [
            new("Art", "KRB"), new("Bezeichnung", "KRB 7"), new("Datum", "2026-11-03"),
            new("Rechtswert", "516296,0"), new("Hochwert", "5640626,0"), new("Ansatzhoehe", "212,8"), new("Endteufe", "3,0"),
            new("Grundwasser", ""), new("Bemerkung", ""),
            new("Schichten[0].Von", "0,00"), new("Schichten[0].Bis", "3,00"), new("Schichten[0].Kurzzeichen", "U, t"),
            new("Schichten[0].Beschreibung", "Auenlehm"), new("Schichten[0].Bodengruppe", "TL"), new("Schichten[0].Kf", "5E-08"),
        ];
    }

    private async Task<HttpResponseMessage> PostFeldAsync(int id, List<KeyValuePair<string, string>> fields)
    {
        var client = NewClient();
        var page = await client.GetStringAsync($"/Projekte/{id}/Feld");
        var token = Regex.Match(page, "name=\"__RequestVerificationToken\"[^>]*value=\"([^\"]+)\"").Groups[1].Value;
        Assert.NotEmpty(token);
        fields.Add(new("__RequestVerificationToken", token));
        return await client.PostAsync($"/Projekte/{id}/Feld", new FormUrlEncodedContent(fields));
    }

    [Fact]
    public async Task Feld_AtTheLimit_IsRefused_WithFieldError_AndSavesNothing()
    {
        int id = await NewProjectAsync();
        await SeedAsync(id, DatenModel.MaxAufschluesseProProjekt);

        var response = await PostFeldAsync(id, Krb7());
        var html = Decode(await response.Content.ReadAsStringAsync());

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Contains("Das Projekt enthält bereits 200 Aufschlüsse; weitere können nicht gespeichert werden.", html);
        Assert.Equal(200, await CountAsync(id));
    }

    [Fact]
    public async Task Feld_BelowTheLimit_SavesTheNewAufschluss()
    {
        int id = await NewProjectAsync();
        await SeedAsync(id, DatenModel.MaxAufschluesseProProjekt - 1);

        var response = await PostFeldAsync(id, Krb7());

        Assert.Equal(HttpStatusCode.Redirect, response.StatusCode);
        Assert.Equal(DatenModel.MaxAufschluesseProProjekt, await CountAsync(id));
    }
}
