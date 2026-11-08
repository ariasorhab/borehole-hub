using System.Globalization;
using System.Text;

namespace BoreholeHub.Web.Drawing;

/// <summary>
/// Shared geometry of the borehole log and the ramming chart. Both drawings use the same header height,
/// the same pixels per metre and the same total height for a given depth scale, so that the depth axes line up
/// exactly when the two SVGs are shown side by side at the same display scale.
/// </summary>
internal static class DrawingLayout
{
    public const double HeaderH = 64;
    public const double TopPad = 34;
    public const double BottomPad = 46;
    public const double PxPerM = 100;

    public static readonly CultureInfo De = CultureInfo.GetCultureInfo("de-DE");

    public static double Height(double scaleMaxM) => HeaderH + TopPad + scaleMaxM * PxPerM + BottomPad;

    public static double Y(double depthM) => HeaderH + TopPad + depthM * PxPerM;

    /// <summary>Invariant, dot-separated number for SVG attributes.</summary>
    public static string N(double v) => Math.Round(v, 2).ToString("0.##", CultureInfo.InvariantCulture);

    public static string Esc(string text) =>
        text.Replace("&", "&amp;").Replace("<", "&lt;").Replace(">", "&gt;").Replace("\"", "&quot;");

    public static string Fmt(double v, int decimals) => v.ToString("F" + decimals, De);

    public static double ScaleFor(params double[] depthsM) => Math.Ceiling(Math.Max(0.5, depthsM.Max()) * 2) / 2;

    public static StringBuilder Header(StringBuilder sb, double width, string line1, string line2)
    {
        sb.Append($"<rect class=\"dr-head\" x=\"0.5\" y=\"0.5\" width=\"{N(width - 1)}\" height=\"{N(HeaderH - 8)}\" rx=\"8\"/>");
        sb.Append($"<text class=\"dr-title\" x=\"16\" y=\"25\">{Esc(line1)}</text>");
        sb.Append($"<text class=\"dr-sub\" x=\"16\" y=\"45\">{Esc(line2)}</text>");
        return sb;
    }
}
