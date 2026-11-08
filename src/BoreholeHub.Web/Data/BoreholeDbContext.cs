using BoreholeHub.Core.Entities;
using Microsoft.EntityFrameworkCore;

namespace BoreholeHub.Web.Data;

/// <summary>EF Core context for the BoreholeHub data model. Works with SQL Server and SQLite.</summary>
public class BoreholeDbContext(DbContextOptions<BoreholeDbContext> options) : DbContext(options)
{
    public DbSet<Project> Projects => Set<Project>();

    public DbSet<Aufschluss> Aufschluesse => Set<Aufschluss>();

    public DbSet<Schicht> Schichten => Set<Schicht>();

    public DbSet<Probe> Proben => Set<Probe>();

    public DbSet<Laborwert> Laborwerte => Set<Laborwert>();

    public DbSet<Grenzwert> Grenzwerte => Set<Grenzwert>();

    public DbSet<RammMesswert> RammMesswerte => Set<RammMesswert>();

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        modelBuilder.Entity<Project>(entity =>
        {
            entity.Property(p => p.Name).IsRequired().HasMaxLength(200);
            entity.Property(p => p.Ort).HasMaxLength(100);
            entity.Property(p => p.Auftraggeber).HasMaxLength(200);
            entity.Property(p => p.Beschreibung);

            entity.HasMany(p => p.Aufschluesse)
                .WithOne(a => a.Project)
                .HasForeignKey(a => a.ProjectId)
                .OnDelete(DeleteBehavior.Cascade);
        });

        modelBuilder.Entity<Aufschluss>(entity =>
        {
            entity.Property(a => a.Bezeichnung).IsRequired().HasMaxLength(20);

            entity.HasMany(a => a.Schichten)
                .WithOne(s => s.Aufschluss)
                .HasForeignKey(s => s.AufschlussId)
                .OnDelete(DeleteBehavior.Cascade);

            entity.HasMany(a => a.Proben)
                .WithOne(p => p.Aufschluss)
                .HasForeignKey(p => p.AufschlussId)
                .OnDelete(DeleteBehavior.Cascade);

            entity.HasMany(a => a.Messwerte)
                .WithOne(m => m.Aufschluss)
                .HasForeignKey(m => m.AufschlussId)
                .OnDelete(DeleteBehavior.Cascade);
        });

        modelBuilder.Entity<Schicht>(entity =>
        {
            entity.Property(s => s.Kurzzeichen).IsRequired().HasMaxLength(40);
            entity.Property(s => s.Beschreibung).IsRequired().HasMaxLength(400);
            entity.Property(s => s.Bodengruppe).HasMaxLength(10);
            entity.Property(s => s.Farbe).HasMaxLength(60);
            entity.Property(s => s.Zustand).HasMaxLength(60);
            entity.Property(s => s.Homogenbereich).HasMaxLength(10);
            entity.Property(s => s.Frostklasse).HasMaxLength(2);
        });

        modelBuilder.Entity<Probe>(entity =>
        {
            entity.Property(p => p.Bezeichnung).IsRequired().HasMaxLength(20);

            entity.HasMany(p => p.Laborwerte)
                .WithOne(l => l.Probe)
                .HasForeignKey(l => l.ProbeId)
                .OnDelete(DeleteBehavior.Cascade);
        });

        modelBuilder.Entity<Laborwert>(entity =>
        {
            entity.Property(l => l.Parameter).IsRequired().HasMaxLength(60);
            entity.Property(l => l.Einheit).IsRequired().HasMaxLength(20);
        });

        modelBuilder.Entity<Grenzwert>(entity =>
        {
            entity.Property(g => g.Parameter).IsRequired().HasMaxLength(60);
            entity.Property(g => g.Einheit).IsRequired().HasMaxLength(20);
            entity.Property(g => g.Klasse).IsRequired().HasMaxLength(20);
        });

        // RammMesswert uses the cascade relation configured on Aufschluss.Messwerte above.
    }
}
