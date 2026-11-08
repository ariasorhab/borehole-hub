using BoreholeHub.Core.Services;

namespace BoreholeHub.Tests;

public class CoordinateServiceTests
{
    [Fact]
    public void ToWgs84_ConvertsKnownPointToLatitudeAndLongitude()
    {
        var (lat, lon) = CoordinateService.ToWgs84(520000, 5640000);

        Assert.InRange(lat, 50.91 - 0.01, 50.91 + 0.01);
        Assert.InRange(lon, 9.28 - 0.01, 9.28 + 0.01);
    }

    [Theory]
    [InlineData(520000, 5640000)]
    [InlineData(516831, 5640494)]
    [InlineData(512869, 5638721)]
    [InlineData(535000, 5660000)]
    public void RoundTrip_ReturnsOriginalCoordinatesWithinHalfAMetre(double rechtswert, double hochwert)
    {
        var (lat, lon) = CoordinateService.ToWgs84(rechtswert, hochwert);
        var (back_r, back_h) = CoordinateService.ToUtm32(lat, lon);

        Assert.InRange(back_r, rechtswert - 0.5, rechtswert + 0.5);
        Assert.InRange(back_h, hochwert - 0.5, hochwert + 0.5);
    }
}
