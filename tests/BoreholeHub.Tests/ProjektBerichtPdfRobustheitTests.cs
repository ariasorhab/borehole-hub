using System.Text;
using BoreholeHub.Core.Entities;
using BoreholeHub.Web.Data;
using BoreholeHub.Web.Reports;
using Microsoft.EntityFrameworkCore;

namespace BoreholeHub.Tests;

/// <summary>
/// The PDF report must not fail when the default cross-section (Anlage 3) changes, e.g. after a borehole is added near the
/// edge of the site. Each case gets its own database through <see cref="WebAppFactory"/>.
/// </summary>
public class ProjektBerichtPdfRobustheitTests
{
    private const double SiteE = 516290, SiteN = 5640622;

    /// <summary>Loads the project the way the PDF handler does and creates the report.</summary>
    private static async Task<byte[]> PdfAsync(WebAppFactory factory, int id)
    {
        Project? projekt = null;
        List<Grenzwert> grenzwerte = [];
        await factory.WithDbAsync(async db =>
        {
            projekt = await db.Projects.AsNoTracking()
                .Include(p => p.Aufschluesse).ThenInclude(a => a.Schichten)
                .Include(p => p.Aufschluesse).ThenInclude(a => a.Proben).ThenInclude(pr => pr.Laborwerte)
                .Include(p => p.Aufschluesse).ThenInclude(a => a.Messwerte)
                .AsSplitQuery()
                .FirstAsync(p => p.Id == id);
            grenzwerte = await db.Grenzwerte.AsNoTracking().OrderBy(g => g.Reihenfolge).ToListAsync();
        });

        return ProjektBerichtPdf.Erzeugen(projekt!, grenzwerte.Count == 0 ? null : grenzwerte);
    }

    private static int CountPages(byte[] pdf) =>
        System.Text.RegularExpressions.Regex.Matches(Encoding.Latin1.GetString(pdf), @"/Type\s*/Page(?![A-Za-z])").Count;

    private static async Task AddAsync(WebAppFactory factory, int projectId, params Aufschluss[] krb)
    {
        await factory.WithDbAsync(async db =>
        {
            foreach (var a in krb)
            {
                a.ProjectId = projectId;
                db.Aufschluesse.Add(a);
            }

            await db.SaveChangesAsync();
        });
    }

    /// <summary>A Kleinrammbohrung with the layer pattern of KRB 7: fill "U, h" to 0,3 m, then "U, t" with kf 5e-8 m/s.</summary>
    private static Aufschluss Krb(string name, double rechts, double hoch, double endteufe, string? homogenbereichOben = null, string? homogenbereichUnten = null)
    {
        var a = new Aufschluss
        {
            Bezeichnung = name,
            Art = AufschlussArt.Kleinrammbohrung,
            Rechtswert = rechts,
            Hochwert = hoch,
            AnsatzhoeheMNhn = 212.8,
            EndteufeM = endteufe,
            GrundwasserM = null,
            Datum = new DateOnly(2026, 11, 9),
        };
        a.Schichten.Add(new Schicht
        {
            VonM = 0, BisM = 0.3, Kurzzeichen = "U, h", Beschreibung = "Auffüllung, schluffig, humos", Bodengruppe = "OH",
            Farbe = "dunkelbraun", Zustand = "weich", Homogenbereich = homogenbereichOben,
        });
        a.Schichten.Add(new Schicht
        {
            VonM = 0.3, BisM = endteufe, Kurzzeichen = "U, t", Beschreibung = "Schluff, tonig", Bodengruppe = "TL",
            Farbe = "braun", Zustand = "steif", Homogenbereich = homogenbereichUnten, KfWertMs = 5e-8,
        });
        return a;
    }

    [Fact]
    public async Task KrbSeven_NextToDemoProject_ReturnsPdf_AndDoesNotFail()
    {
        using var factory = new WebAppFactory();
        int id = await SeedDemoAsync(factory);
        await AddAsync(factory, id, Krb("KRB 7", 516314, 5640622, 2.0));

        byte[] pdf = await PdfAsync(factory, id);

        Assert.Equal("%PDF", Encoding.ASCII.GetString(pdf, 0, 4));
        Assert.True(pdf.Length > 1000);
        // Deckblatt + Lageplan + 7 Bohrprofile + Profilschnitt + Labor + Versickerung
        Assert.True(CountPages(pdf) >= 1 + 1 + 7 + 1 + 1 + 1, $"pages: {CountPages(pdf)}");
    }

    public static TheoryData<string, double, double, double> ExtraPositions => new()
    {
        { "25 m ostwaerts", SiteE + 25, SiteN, 1.0 },
        { "25 m westwaerts", SiteE - 25, SiteN, 2.0 },
        { "60 m nordwaerts", SiteE, SiteN + 60, 3.0 },
        { "60 m suedwaerts", SiteE, SiteN - 60, 4.0 },
        { "150 m ost und nord", SiteE + 150, SiteN + 150, 1.5 },
        { "150 m west und sued", SiteE - 150, SiteN - 150, 4.0 },
        { "150 m ost und sued", SiteE + 150, SiteN - 150, 2.5 },
    };

    [Theory]
    [MemberData(nameof(ExtraPositions))]
    public async Task ExtraKrb_AroundOrBeyondSite_ReturnsPdf_AndDoesNotFail(string lage, double rechts, double hoch, double endteufe)
    {
        using var factory = new WebAppFactory();
        int id = await SeedDemoAsync(factory);
        await AddAsync(factory, id, Krb($"KRB 7 ({lage})", rechts, hoch, endteufe, "B1", "B2"));

        byte[] pdf = await PdfAsync(factory, id);

        Assert.Equal("%PDF", Encoding.ASCII.GetString(pdf, 0, 4));
        Assert.True(pdf.Length > 1000, $"pdf bytes: {pdf.Length}");
        Assert.True(CountPages(pdf) >= 1 + 1 + 7 + 1 + 1 + 1, $"pages: {CountPages(pdf)}");
    }

    [Fact]
    public async Task ProjectWithExactlyTwoKrb_ReturnsPdf_AndDoesNotFail()
    {
        using var factory = new WebAppFactory();
        int id = 0;
        await factory.WithDbAsync(async db =>
        {
            var projekt = new Project
            {
                Name = "Zweipunkt-Projekt (Demo)",
                Ort = "Schwalmstadt-Ziegenhain",
                Auftraggeber = "Musterbau GmbH (fiktiv)",
                ErstelltAm = new DateTime(2026, 11, 9),
            };
            db.Projects.Add(projekt);
            await db.SaveChangesAsync();
            id = projekt.Id;
        });
        await AddAsync(factory, id, Krb("KRB 1", SiteE - 120, SiteN - 80, 2.0), Krb("KRB 2", SiteE + 130, SiteN + 90, 3.0));

        byte[] pdf = await PdfAsync(factory, id);

        Assert.Equal("%PDF", Encoding.ASCII.GetString(pdf, 0, 4));
        Assert.True(CountPages(pdf) >= 1 + 1 + 2 + 1 + 1 + 1, $"pages: {CountPages(pdf)}");
    }

    private static async Task<int> SeedDemoAsync(WebAppFactory factory)
    {
        int id = 0;
        await factory.WithDbAsync(async db => id = await DemoDataSeeder.SeedAsync(db));
        return id;
    }
}
