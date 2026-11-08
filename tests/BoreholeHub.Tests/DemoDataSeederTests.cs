using BoreholeHub.Core.Entities;
using BoreholeHub.Core.Services;
using BoreholeHub.Web.Data;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;

namespace BoreholeHub.Tests;

public class DemoDataSeederTests : IDisposable
{
    private readonly SqliteConnection _connection = new("DataSource=:memory:");
    private readonly DbContextOptions<BoreholeDbContext> _options;

    public DemoDataSeederTests()
    {
        // The in-memory database lives as long as this connection stays open.
        _connection.Open();
        _options = new DbContextOptionsBuilder<BoreholeDbContext>().UseSqlite(_connection).Options;
        using var db = new BoreholeDbContext(_options);
        db.Database.EnsureCreated();
    }

    public void Dispose() => _connection.Dispose();

    private async Task<int> SeedAsync()
    {
        using var db = new BoreholeDbContext(_options);
        return await DemoDataSeeder.SeedAsync(db);
    }

    private BoreholeDbContext Open() => new(_options);

    [Fact]
    public async Task Seed_CreatesOneProject_WithFiveKrbAndThreeDph()
    {
        int id = await SeedAsync();

        using var db = Open();
        var project = await db.Projects.Include(p => p.Aufschluesse).SingleAsync();
        Assert.Equal(id, project.Id);
        Assert.Equal(DemoDataSeeder.DemoProjectName, project.Name);
        Assert.Equal(8, project.Aufschluesse.Count);
        Assert.Equal(5, project.Aufschluesse.Count(a => a.Art == AufschlussArt.Kleinrammbohrung));
        Assert.Equal(3, project.Aufschluesse.Count(a => a.Art == AufschlussArt.Rammsondierung));
        Assert.Equal(2, project.Aufschluesse.Count(a => a.GrundwasserM is not null));
        Assert.All(project.Aufschluesse.Where(a => a.GrundwasserM is not null), a => Assert.InRange(a.GrundwasserM!.Value, 2.6, 3.2));
    }

    [Fact]
    public async Task Seed_CalledTwice_ReturnsSameId_AndDoesNotDuplicate()
    {
        int first = await SeedAsync();
        int second = await SeedAsync();

        Assert.Equal(first, second);
        using var db = Open();
        Assert.Equal(1, await db.Projects.CountAsync());
        Assert.Equal(8, await db.Aufschluesse.CountAsync());
        Assert.Equal(60, await db.Grenzwerte.CountAsync());
    }

    [Fact]
    public async Task Seed_KrbLayers_StartAtZero_EndAtEndteufe_WithoutGapsOrOverlaps()
    {
        await SeedAsync();

        using var db = Open();
        var krbs = await db.Aufschluesse.Include(a => a.Schichten)
            .Where(a => a.Art == AufschlussArt.Kleinrammbohrung).ToListAsync();

        Assert.Equal(5, krbs.Count);
        foreach (var krb in krbs)
        {
            var layers = krb.Schichten.OrderBy(s => s.VonM).ToList();
            Assert.InRange(layers.Count, 3, 5);
            Assert.InRange(krb.EndteufeM, 3.0, 4.0);
            Assert.Equal(0.0, layers[0].VonM, 3);
            Assert.Equal(krb.EndteufeM, layers[^1].BisM, 3);
            for (int i = 1; i < layers.Count; i++)
            {
                Assert.Equal(layers[i - 1].BisM, layers[i].VonM, 3);
            }

            Assert.All(layers, s => Assert.True(s.BisM > s.VonM, $"{krb.Bezeichnung}: empty layer"));
        }
    }

    [Fact]
    public async Task Seed_EveryDph_Has40Messwerte_In10cmSteps()
    {
        await SeedAsync();

        using var db = Open();
        var dphs = await db.Aufschluesse.Include(a => a.Messwerte)
            .Where(a => a.Art == AufschlussArt.Rammsondierung).ToListAsync();

        Assert.Equal(3, dphs.Count);
        foreach (var dph in dphs)
        {
            var werte = dph.Messwerte.OrderBy(m => m.TiefeBisM).ToList();
            Assert.Equal(40, werte.Count);
            for (int i = 0; i < 40; i++)
            {
                Assert.Equal((i + 1) * 0.1, werte[i].TiefeBisM, 6);
            }

            Assert.All(werte, m => Assert.InRange(m.N10, 1, 20));
            // Gravel below the loam is denser than the loam above it.
            Assert.True(werte.Skip(30).Average(m => m.N10) > werte.Skip(5).Take(8).Average(m => m.N10));
        }
    }

    [Fact]
    public async Task Seed_LabCheck_FlagsBleiInExactlyOneSample()
    {
        await SeedAsync();

        using var db = Open();
        var proben = await db.Proben.Include(p => p.Laborwerte).ToListAsync();
        var limits = await db.Grenzwerte.ToListAsync();
        Assert.All(limits, g => Assert.True(g.IstBeispiel));

        int worseThanBm0 = 0;
        foreach (var probe in proben)
        {
            var blei = probe.Laborwerte.Where(l => l.Parameter == "Blei");
            var result = LabCheckService.Evaluate(blei, limits).Single();
            if (result.Ueberschritten || result.EingehalteneKlasse != "BM-0")
            {
                worseThanBm0++;
                Assert.Equal("KRB 1/1", probe.Bezeichnung);
            }
        }

        Assert.Equal(1, worseThanBm0);

        // PAK16 of the same fill sample is also above BM-0.
        var fill = proben.Single(p => p.Bezeichnung == "KRB 1/1");
        var pak = LabCheckService.Evaluate(fill.Laborwerte.Where(l => l.Parameter == "PAK16"), limits).Single();
        Assert.NotEqual("BM-0", pak.EingehalteneKlasse);
    }

    [Fact]
    public async Task Seed_Grenzwerte_HaveSixClassesPerParameter_InIncreasingOrder()
    {
        await SeedAsync();

        using var db = Open();
        var limits = await db.Grenzwerte.ToListAsync();
        var byParameter = limits.GroupBy(g => g.Parameter).ToList();

        Assert.Equal(10, byParameter.Count);
        foreach (var group in byParameter)
        {
            var ordered = group.OrderBy(g => g.Reihenfolge).ToList();
            Assert.Equal(["BM-0", "BM-0*", "BM-F0*", "BM-F1", "BM-F2", "BM-F3"], ordered.Select(g => g.Klasse));
            for (int i = 1; i < ordered.Count; i++)
            {
                Assert.True(ordered[i].Wert > ordered[i - 1].Wert, $"{group.Key}: not increasing");
            }
        }
    }

    [Fact]
    public async Task Seed_Coordinates_ConvertToSchwalmstadtRange()
    {
        await SeedAsync();

        using var db = Open();
        var all = await db.Aufschluesse.ToListAsync();

        Assert.Equal(8, all.Count);
        foreach (var a in all)
        {
            var (lat, lon) = CoordinateService.ToWgs84(a.Rechtswert, a.Hochwert);
            Assert.InRange(lat, 50.89, 50.91);
            Assert.InRange(lon, 9.17, 9.20);
            Assert.InRange(a.AnsatzhoeheMNhn, 214.0, 216.0);
        }
    }

    [Fact]
    public async Task Seed_SamplesLieInsideTheirBorehole_AndHaveTenLabValues()
    {
        await SeedAsync();

        using var db = Open();
        var proben = await db.Proben.Include(p => p.Laborwerte).Include(p => p.Aufschluss).ToListAsync();

        Assert.InRange(proben.Count, 5, 10);
        foreach (var probe in proben)
        {
            Assert.Equal(10, probe.Laborwerte.Count);
            Assert.True(probe.BisM <= probe.Aufschluss.EndteufeM);
            Assert.StartsWith(probe.Aufschluss.Bezeichnung, probe.Bezeichnung);
        }
    }
}
