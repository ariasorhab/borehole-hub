using BoreholeHub.Core.Entities;
using BoreholeHub.Core.Services;

namespace BoreholeHub.Tests;

public class LabCheckServiceTests
{
    // Example limits only (not official values).
    private static List<Grenzwert> BleiGrenzwerte() =>
    [
        new Grenzwert { Parameter = "Blei", Einheit = "mg/kg", Klasse = "Z0", Wert = 40, Reihenfolge = 1 },
        new Grenzwert { Parameter = "Blei", Einheit = "mg/kg", Klasse = "Z1.1", Wert = 140, Reihenfolge = 2 },
        new Grenzwert { Parameter = "Blei", Einheit = "mg/kg", Klasse = "Z2", Wert = 700, Reihenfolge = 3 },
    ];

    private static Laborwert Wert(string parameter, double wert, string einheit = "mg/kg") =>
        new() { Parameter = parameter, Wert = wert, Einheit = einheit };

    [Fact]
    public void Evaluate_ValueUnderStrictestLimit_GetsStrictestClass()
    {
        var results = LabCheckService.Evaluate([Wert("Blei", 30)], BleiGrenzwerte());

        var r = Assert.Single(results);
        Assert.Equal("Z0", r.EingehalteneKlasse);
        Assert.False(r.Ueberschritten);
        Assert.Null(r.UeberschritteneGrenze);
    }

    [Fact]
    public void Evaluate_ValueBetweenClasses_GetsNextClass()
    {
        var results = LabCheckService.Evaluate([Wert("Blei", 120)], BleiGrenzwerte());

        var r = Assert.Single(results);
        Assert.Equal("Z1.1", r.EingehalteneKlasse);
        Assert.False(r.Ueberschritten);
    }

    [Fact]
    public void Evaluate_ValueAboveAllLimits_IsExceededWithLeastStrictClass()
    {
        var results = LabCheckService.Evaluate([Wert("Blei", 800)], BleiGrenzwerte());

        var r = Assert.Single(results);
        Assert.Null(r.EingehalteneKlasse);
        Assert.True(r.Ueberschritten);
        Assert.Equal("Z2", r.UeberschritteneGrenze);
    }

    [Fact]
    public void Evaluate_ParameterWithoutLimits_IsNotExceeded()
    {
        var results = LabCheckService.Evaluate([Wert("Arsen", 12)], BleiGrenzwerte());

        var r = Assert.Single(results);
        Assert.Null(r.EingehalteneKlasse);
        Assert.False(r.Ueberschritten);
        Assert.Null(r.UeberschritteneGrenze);
    }

    [Fact]
    public void Evaluate_ParameterAndUnitMatchIgnoresCase()
    {
        var results = LabCheckService.Evaluate([Wert("BLEI", 120, "MG/KG")], BleiGrenzwerte());

        var r = Assert.Single(results);
        Assert.Equal("Z1.1", r.EingehalteneKlasse);
        Assert.Equal("BLEI", r.Parameter);
    }

    [Fact]
    public void Evaluate_UnitMismatch_TreatsParameterAsWithoutLimits()
    {
        var results = LabCheckService.Evaluate([Wert("Blei", 120, "mg/l")], BleiGrenzwerte());

        var r = Assert.Single(results);
        Assert.Null(r.EingehalteneKlasse);
        Assert.False(r.Ueberschritten);
    }

    [Fact]
    public void Evaluate_ValueExactlyAtLimit_MeetsThatClass()
    {
        var results = LabCheckService.Evaluate([Wert("Blei", 40)], BleiGrenzwerte());

        Assert.Equal("Z0", Assert.Single(results).EingehalteneKlasse);
    }
}
