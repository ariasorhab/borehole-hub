using BoreholeHub.Core.Entities;
using BoreholeHub.Web.Data;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;

namespace BoreholeHub.Tests;

public class BoreholeDbContextTests
{
    private static DbContextOptions<BoreholeDbContext> OptionsFor(SqliteConnection connection) =>
        new DbContextOptionsBuilder<BoreholeDbContext>().UseSqlite(connection).Options;

    [Fact]
    public void Cascade_DeletingProject_RemovesAllChildRows()
    {
        // The in-memory SQLite database lives as long as this connection stays open.
        using var connection = new SqliteConnection("DataSource=:memory:");
        connection.Open();
        var options = OptionsFor(connection);

        int projectId;
        using (var setup = new BoreholeDbContext(options))
        {
            setup.Database.EnsureCreated();

            var project = new Project
            {
                Name = "Testprojekt",
                Ort = "Schwalmstadt",
                Auftraggeber = "Muster GmbH",
                ErstelltAm = new DateTime(2026, 11, 8),
            };

            var aufschluss = new Aufschluss
            {
                Bezeichnung = "KRB 1",
                Art = AufschlussArt.Kleinrammbohrung,
                Rechtswert = 520000,
                Hochwert = 5640000,
                AnsatzhoeheMNhn = 230.0,
                EndteufeM = 4.0,
                Datum = new DateOnly(2026, 11, 8),
            };

            aufschluss.Schichten.Add(new Schicht
            {
                VonM = 0,
                BisM = 1.5,
                Kurzzeichen = "U, s', t",
                Beschreibung = "Auffüllung, schluffig, sandig",
            });

            var probe = new Probe { Bezeichnung = "KRB 1/1", VonM = 0.5, BisM = 1.0 };
            probe.Laborwerte.Add(new Laborwert { Parameter = "Blei", Wert = 30, Einheit = "mg/kg" });
            aufschluss.Proben.Add(probe);

            aufschluss.Messwerte.Add(new RammMesswert { TiefeBisM = 0.1, N10 = 4 });

            project.Aufschluesse.Add(aufschluss);
            setup.Projects.Add(project);
            setup.SaveChanges();
            projectId = project.Id;
        }

        // Read back with Include in a fresh context.
        using (var read = new BoreholeDbContext(options))
        {
            var loaded = read.Projects
                .Include(p => p.Aufschluesse).ThenInclude(a => a.Schichten)
                .Include(p => p.Aufschluesse).ThenInclude(a => a.Proben).ThenInclude(p => p.Laborwerte)
                .Include(p => p.Aufschluesse).ThenInclude(a => a.Messwerte)
                .Single(p => p.Id == projectId);

            var aufschluss = Assert.Single(loaded.Aufschluesse);
            Assert.Equal("KRB 1", aufschluss.Bezeichnung);
            Assert.Single(aufschluss.Schichten);
            var probe = Assert.Single(aufschluss.Proben);
            Assert.Single(probe.Laborwerte);
            Assert.Single(aufschluss.Messwerte);
        }

        // Delete the project directly in the database so that only the schema's cascade applies.
        using (var delete = new BoreholeDbContext(options))
        {
            delete.Projects.Where(p => p.Id == projectId).ExecuteDelete();
        }

        using (var check = new BoreholeDbContext(options))
        {
            Assert.Empty(check.Projects);
            Assert.Empty(check.Aufschluesse);
            Assert.Empty(check.Schichten);
            Assert.Empty(check.Proben);
            Assert.Empty(check.Laborwerte);
            Assert.Empty(check.RammMesswerte);
        }
    }
}
