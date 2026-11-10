using System.Globalization;
using System.Net;
using System.Xml.Linq;
using BoreholeHub.Core.Entities;
using BoreholeHub.Core.Services;
using BoreholeHub.Web.Data;
using BoreholeHub.Web.Localization;

namespace BoreholeHub.Tests;

/// <summary>DE/EN checks for the Labor and Prüfung pages and for the data check messages (T45, D128 3A).</summary>
public class LocalizationLaborPruefungTests : IClassFixture<WebAppFactory>
{
    private static readonly CultureInfo De = CultureInfo.GetCultureInfo("de-DE");

    private readonly WebAppFactory _factory;

    public LocalizationLaborPruefungTests(WebAppFactory factory) => _factory = factory;

    // ---------- helpers ----------

    private static Schicht S(double von, double bis, string? bodengruppe = "TL", double? kf = null) => new()
    {
        VonM = von, BisM = bis, Kurzzeichen = "U, t", Beschreibung = "Test", Bodengruppe = bodengruppe, KfWertMs = kf,
    };

    /// <summary>A Kleinrammbohrung in the middle of Hessen unless coordinates or height are given.</summary>
    private static Aufschluss Krb(int id, string bezeichnung, double endteufe, double rechtswert = 516000, double hochwert = 5640000,
        double hoehe = 200, params Schicht[] schichten) => new()
    {
        Id = id,
        Bezeichnung = bezeichnung,
        Art = AufschlussArt.Kleinrammbohrung,
        Rechtswert = rechtswert,
        Hochwert = hochwert,
        AnsatzhoeheMNhn = hoehe,
        EndteufeM = endteufe,
        Datum = new DateOnly(2026, 11, 1),
        Schichten = [.. schichten],
    };

    private static Aufschluss Dph(int id, string bezeichnung, double endteufe, params RammMesswert[] messwerte) => new()
    {
        Id = id,
        Bezeichnung = bezeichnung,
        Art = AufschlussArt.Rammsondierung,
        Rechtswert = 516000,
        Hochwert = 5640000,
        AnsatzhoeheMNhn = 200,
        EndteufeM = endteufe,
        Datum = new DateOnly(2026, 11, 1),
        Messwerte = [.. messwerte],
    };

    private static RammMesswert M(double tiefe, int n10 = 5) => new() { TiefeBisM = tiefe, N10 = n10 };

    /// <summary>One Aufschluss per rule R1 to R7, so that every rule emits at least one finding.</summary>
    private static IReadOnlyList<PruefBefund> AlleRegeln()
    {
        var k1 = Krb(1, "KRB 1", 4.5, schichten: [S(0.0, 1.0, "SE"), S(1.2, 3.0)]);
        k1.Proben = [new Probe { Bezeichnung = "KRB 1/9", VonM = 4.0, BisM = 5.0 }];
        var k2 = Krb(2, "KRB 2", 3.0, rechtswert: 100, hoehe: 30, schichten: [S(0.0, 3.0)]);
        var k3 = Krb(3, "KRB 3", 0.0);
        var d4 = Dph(4, "DPH 4", 1.0, M(0.1, 5), M(0.2, -1), M(0.5, 60));
        var d5 = Dph(5, "DPH 5", 3.0);
        return DataQualityService.Pruefen([k1, k2, k3, d4, d5]);
    }

    private static Dictionary<string, string> EnglischeWerte()
    {
        string root = FindWebRoot();
        string resx = Path.Combine(root, "src", "BoreholeHub.Web", "Resources", "Localization", "PruefTexte.en.resx");
        return XDocument.Load(resx).Descendants("data")
            .ToDictionary(d => (string)d.Attribute("name")!, d => (string?)d.Element("value") ?? string.Empty);
    }

    private static string FindWebRoot()
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir is not null && !Directory.Exists(Path.Combine(dir.FullName, "src", "BoreholeHub.Web")))
        {
            dir = dir.Parent;
        }
        return dir?.FullName ?? throw new DirectoryNotFoundException("src/BoreholeHub.Web was not found above the test output folder.");
    }

    private async Task<int> SeedDemoAsync()
    {
        int id = 0;
        await _factory.WithDbAsync(async db => id = await DemoDataSeeder.SeedAsync(db));
        return id;
    }

    private async Task<int> NewProjectAsync(string name, params Aufschluss[] aufschluesse)
    {
        int id = 0;
        await _factory.WithDbAsync(async db =>
        {
            var p = new Project { Name = name, Ort = "Test", Auftraggeber = "Test", ErstelltAm = new DateTime(2026, 11, 2) };
            db.Projects.Add(p);
            await db.SaveChangesAsync();
            id = p.Id;
            foreach (var a in aufschluesse)
            {
                a.ProjectId = id;
                db.Aufschluesse.Add(a);
            }

            await db.SaveChangesAsync();
        });
        return id;
    }

    // ---------- tests ----------

    [Fact]
    public void Core_Meldung_EqualsFormattedTemplate_ForFindingsOfEveryRule()
    {
        var befunde = AlleRegeln();

        Assert.Equal(7, befunde.Select(b => b.Regel).Distinct().Count());
        foreach (var b in befunde)
        {
            Assert.False(string.IsNullOrEmpty(b.Vorlage), $"no template for '{b.Meldung}'");
            Assert.Equal(string.Format(De, b.Vorlage, b.Argumente.ToArray<object>()), b.Meldung);
        }
    }

    [Fact]
    public void EveryTemplate_EmittedByTheCheck_IsListed_AndHasEnglishEntry()
    {
        var befunde = AlleRegeln();
        var englisch = EnglischeWerte();

        foreach (var b in befunde)
        {
            Assert.Contains(b.Vorlage, PruefTexte.Vorlagen);
        }
        foreach (var vorlage in PruefTexte.Vorlagen)
        {
            Assert.True(englisch.ContainsKey(vorlage) && englisch[vorlage].Length > 0, $"no English entry for '{vorlage}'");
        }
    }

    [Fact]
    public void PruefBefund_Equality_IgnoresTemplateAndArguments()
    {
        var mit = new PruefBefund("Schichtfolge", PruefStufe.Fehler, 9, "KRB 9", "Lücke 1,00–1,20 m.")
        {
            Vorlage = "Lücke {0}–{1} m.",
            Argumente = ["1,00", "1,20"],
        };

        Assert.Equal(new PruefBefund("Schichtfolge", PruefStufe.Fehler, 9, "KRB 9", "Lücke 1,00–1,20 m."), mit);
    }

    [Fact]
    public async Task Pruefung_English_ShowsEnglishHeadingsAndFindings()
    {
        var broken = Krb(0, "KRB 9", 3.0, schichten: [S(0.0, 1.0), S(1.2, 3.0)]);
        int id = await NewProjectAsync("Data check test", broken);
        var client = await LanguageTestHelper.CreateEnglishClientAsync(_factory);

        var html = WebUtility.HtmlDecode(await client.GetStringAsync($"/Projekte/{id}/Pruefung"));

        Assert.Contains("<html lang=\"en\"", html);
        Assert.Contains("<h1>Data check</h1>", html);
        Assert.Contains("Gap 1,00–1,20 m.", html);
        Assert.Contains("<span class=\"badge badge-bad\">Error</span>", html);
        Assert.Contains("<th scope=\"col\">Rule</th>", html);
        Assert.Contains("Automatic plausibility check of the recorded data.", html);
        Assert.DoesNotContain("Lücke 1,00–1,20 m.", html);
        Assert.DoesNotContain("Befunde", html);
    }

    [Fact]
    public async Task Labor_English_ShowsEnglishHeadingsAndNotice()
    {
        int id = await SeedDemoAsync();
        var client = await LanguageTestHelper.CreateEnglishClientAsync(_factory);

        var html = WebUtility.HtmlDecode(await client.GetStringAsync($"/Projekte/{id}/Labor"));

        Assert.Contains("<html lang=\"en\"", html);
        Assert.Contains("<h1>Lab & infiltration</h1>", html);
        Assert.Contains("Example values for demonstration only", html);
        Assert.Contains("Infiltration (pre-check)", html);
        Assert.Contains("Show example limit values", html);
        Assert.DoesNotContain("Vereinfachte Vorprüfung", html);
        Assert.DoesNotContain("Beispielgrenzwerte anzeigen", html);
    }

    [Fact]
    public async Task GermanPages_StayGerman_WithoutCookie()
    {
        var broken = Krb(0, "KRB 9", 3.0, schichten: [S(0.0, 1.0), S(1.2, 3.0)]);
        int id = await NewProjectAsync("Prüfung deutsch", broken);
        // The Labor page needs the limit values of the demo seed (a project without them fails on Klassen[^1]).
        int demoId = await SeedDemoAsync();
        var client = _factory.CreateClient();

        var pruefung = WebUtility.HtmlDecode(await client.GetStringAsync($"/Projekte/{id}/Pruefung"));
        var labor = WebUtility.HtmlDecode(await client.GetStringAsync($"/Projekte/{demoId}/Labor"));

        Assert.Contains("<html lang=\"de\"", pruefung);
        Assert.Contains("<h1>Prüfung</h1>", pruefung);
        Assert.Contains("Lücke 1,00–1,20 m.", pruefung);
        Assert.Contains("<span class=\"badge badge-bad\">Fehler</span>", pruefung);
        Assert.Contains("<h1>Labor & Versickerung</h1>", labor);
        Assert.Contains("Vereinfachte Vorprüfung: Der kf-Wert ersetzt keine Bemessung nach DWA-A 138-1.", labor);
    }
}
