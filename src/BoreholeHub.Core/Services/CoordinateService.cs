namespace BoreholeHub.Core.Services;

/// <summary>
/// Converts between ETRS89 / UTM zone 32N (EPSG:25832) and WGS84 geographic coordinates.
/// Uses the standard Krüger series on the GRS80 ellipsoid (accurate to well below 1 m in zone 32).
/// </summary>
public static class CoordinateService
{
    // GRS80 ellipsoid parameters used by ETRS89.
    private const double SemiMajorAxis = 6378137.0;
    private const double Flattening = 1.0 / 298.257222101;

    // UTM zone 32 parameters.
    private const double ScaleFactor = 0.9996;
    private const double CentralMeridianDeg = 9.0;
    private const double FalseEasting = 500000.0;

    private static readonly double E2 = Flattening * (2.0 - Flattening);
    private static readonly double Ep2 = E2 / (1.0 - E2);

    /// <summary>Converts EPSG:25832 coordinates to WGS84 latitude and longitude in degrees.</summary>
    public static (double Lat, double Lon) ToWgs84(double rechtswert, double hochwert)
    {
        double x = rechtswert - FalseEasting;
        double y = hochwert;

        double m = y / ScaleFactor;
        double mu = m / (SemiMajorAxis * (1.0 - E2 / 4.0 - 3.0 * E2 * E2 / 64.0 - 5.0 * E2 * E2 * E2 / 256.0));

        double e1 = (1.0 - Math.Sqrt(1.0 - E2)) / (1.0 + Math.Sqrt(1.0 - E2));

        // Footpoint latitude.
        double phi1 = mu
            + (3.0 * e1 / 2.0 - 27.0 * Math.Pow(e1, 3) / 32.0) * Math.Sin(2.0 * mu)
            + (21.0 * e1 * e1 / 16.0 - 55.0 * Math.Pow(e1, 4) / 32.0) * Math.Sin(4.0 * mu)
            + (151.0 * Math.Pow(e1, 3) / 96.0) * Math.Sin(6.0 * mu)
            + (1097.0 * Math.Pow(e1, 4) / 512.0) * Math.Sin(8.0 * mu);

        double sinPhi1 = Math.Sin(phi1);
        double cosPhi1 = Math.Cos(phi1);
        double tanPhi1 = Math.Tan(phi1);

        double n1 = SemiMajorAxis / Math.Sqrt(1.0 - E2 * sinPhi1 * sinPhi1);
        double t1 = tanPhi1 * tanPhi1;
        double c1 = Ep2 * cosPhi1 * cosPhi1;
        double r1 = SemiMajorAxis * (1.0 - E2) / Math.Pow(1.0 - E2 * sinPhi1 * sinPhi1, 1.5);
        double d = x / (n1 * ScaleFactor);

        double lat = phi1 - (n1 * tanPhi1 / r1) * (
            d * d / 2.0
            - (5.0 + 3.0 * t1 + 10.0 * c1 - 4.0 * c1 * c1 - 9.0 * Ep2) * Math.Pow(d, 4) / 24.0
            + (61.0 + 90.0 * t1 + 298.0 * c1 + 45.0 * t1 * t1 - 252.0 * Ep2 - 3.0 * c1 * c1) * Math.Pow(d, 6) / 720.0);

        double lon = CentralMeridianDeg * Math.PI / 180.0 + (
            d
            - (1.0 + 2.0 * t1 + c1) * Math.Pow(d, 3) / 6.0
            + (5.0 - 2.0 * c1 + 28.0 * t1 - 3.0 * c1 * c1 + 8.0 * Ep2 + 24.0 * t1 * t1) * Math.Pow(d, 5) / 120.0) / cosPhi1;

        return (lat * 180.0 / Math.PI, lon * 180.0 / Math.PI);
    }

    /// <summary>Converts WGS84 latitude and longitude in degrees to EPSG:25832 coordinates.</summary>
    public static (double Rechtswert, double Hochwert) ToUtm32(double lat, double lon)
    {
        double phi = lat * Math.PI / 180.0;
        double lam = (lon - CentralMeridianDeg) * Math.PI / 180.0;

        double sinPhi = Math.Sin(phi);
        double cosPhi = Math.Cos(phi);
        double tanPhi = Math.Tan(phi);

        double n = SemiMajorAxis / Math.Sqrt(1.0 - E2 * sinPhi * sinPhi);
        double t = tanPhi * tanPhi;
        double c = Ep2 * cosPhi * cosPhi;
        double a = lam * cosPhi;

        // Meridian arc length.
        double m = SemiMajorAxis * (
            (1.0 - E2 / 4.0 - 3.0 * E2 * E2 / 64.0 - 5.0 * Math.Pow(E2, 3) / 256.0) * phi
            - (3.0 * E2 / 8.0 + 3.0 * E2 * E2 / 32.0 + 45.0 * Math.Pow(E2, 3) / 1024.0) * Math.Sin(2.0 * phi)
            + (15.0 * E2 * E2 / 256.0 + 45.0 * Math.Pow(E2, 3) / 1024.0) * Math.Sin(4.0 * phi)
            - (35.0 * Math.Pow(E2, 3) / 3072.0) * Math.Sin(6.0 * phi));

        double easting = ScaleFactor * n * (
            a
            + (1.0 - t + c) * Math.Pow(a, 3) / 6.0
            + (5.0 - 18.0 * t + t * t + 72.0 * c - 58.0 * Ep2) * Math.Pow(a, 5) / 120.0) + FalseEasting;

        double northing = ScaleFactor * (m + n * tanPhi * (
            a * a / 2.0
            + (5.0 - t + 9.0 * c + 4.0 * c * c) * Math.Pow(a, 4) / 24.0
            + (61.0 - 58.0 * t + t * t + 600.0 * c - 330.0 * Ep2) * Math.Pow(a, 6) / 720.0));

        return (easting, northing);
    }
}
