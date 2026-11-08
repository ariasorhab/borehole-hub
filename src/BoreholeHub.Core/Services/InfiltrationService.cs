namespace BoreholeHub.Core.Services;

/// <summary>Classification of a hydraulic conductivity value for infiltration (Versickerung).</summary>
public enum InfiltrationResult
{
    /// <summary>No kf value available.</summary>
    Unbekannt,

    /// <summary>kf above the upper limit.</summary>
    ZuDurchlaessig,

    /// <summary>kf within the range.</summary>
    Geeignet,

    /// <summary>kf below the lower limit.</summary>
    ZuGeringDurchlaessig
}

/// <summary>Assesses kf values for infiltration (Versickerung).</summary>
public static class InfiltrationService
{
    /// <summary>Upper kf limit in m/s. Range per DWA-A 138; verified separately.</summary>
    public const double KfMax = 1e-3;

    /// <summary>Lower kf limit in m/s. Range per DWA-A 138; verified separately.</summary>
    public const double KfMin = 1e-6;

    /// <summary>Assesses a kf value in m/s. Values exactly at KfMax and KfMin are Geeignet.</summary>
    public static InfiltrationResult Assess(double? kfWertMs)
    {
        if (kfWertMs is null)
        {
            return InfiltrationResult.Unbekannt;
        }

        double kf = kfWertMs.Value;

        if (kf > KfMax)
        {
            return InfiltrationResult.ZuDurchlaessig;
        }

        if (kf >= KfMin)
        {
            return InfiltrationResult.Geeignet;
        }

        return InfiltrationResult.ZuGeringDurchlaessig;
    }
}
