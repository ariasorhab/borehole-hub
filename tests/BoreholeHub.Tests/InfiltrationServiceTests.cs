using BoreholeHub.Core.Services;

namespace BoreholeHub.Tests;

public class InfiltrationServiceTests
{
    [Fact]
    public void Assess_NullValue_IsUnbekannt()
    {
        Assert.Equal(InfiltrationResult.Unbekannt, InfiltrationService.Assess(null));
    }

    [Fact]
    public void Assess_ValueAboveUpperLimit_IsZuDurchlaessig()
    {
        Assert.Equal(InfiltrationResult.ZuDurchlaessig, InfiltrationService.Assess(1e-2));
    }

    [Fact]
    public void Assess_ValueInsideRange_IsGeeignet()
    {
        Assert.Equal(InfiltrationResult.Geeignet, InfiltrationService.Assess(1e-4));
    }

    [Fact]
    public void Assess_ValueBelowLowerLimit_IsZuGeringDurchlaessig()
    {
        Assert.Equal(InfiltrationResult.ZuGeringDurchlaessig, InfiltrationService.Assess(1e-7));
    }

    [Fact]
    public void Assess_ExactlyUpperLimit_IsGeeignet()
    {
        Assert.Equal(InfiltrationResult.Geeignet, InfiltrationService.Assess(1e-3));
    }

    [Fact]
    public void Assess_ExactlyLowerLimit_IsGeeignet()
    {
        Assert.Equal(InfiltrationResult.Geeignet, InfiltrationService.Assess(1e-6));
    }

    [Fact]
    public void Assess_JustAboveUpperLimit_IsZuDurchlaessig()
    {
        Assert.Equal(InfiltrationResult.ZuDurchlaessig, InfiltrationService.Assess(1.0001e-3));
    }

    [Fact]
    public void Constants_MatchDwaRange()
    {
        Assert.Equal(1e-3, InfiltrationService.KfMax);
        Assert.Equal(1e-6, InfiltrationService.KfMin);
    }
}
