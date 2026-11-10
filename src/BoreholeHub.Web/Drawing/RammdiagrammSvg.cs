using System.Text;
using BoreholeHub.Core.Entities;
using Microsoft.Extensions.Localization;
using static BoreholeHub.Web.Drawing.DrawingLayout;

namespace BoreholeHub.Web.Drawing;

/// <summary>Blow counts N10 of a heavy dynamic probe as horizontal bars per 10 cm, on the same depth scale as <see cref="BohrprofilSvg"/>.</summary>
public static class RammdiagrammSvg
{
    private const double Width = 500;
    private const double PlotX = 48;
    private const double PlotW = 416;

    /// <summary>
    /// Renders the probing chart. <paramref name="loc"/> is the page model's localizer (keys in DetailsModel.en.resx);
    /// null gives the German text, as used by the PDF report.
    /// </summary>
    public static string Render(Aufschluss dph, IReadOnlyList<RammMesswert> werte, double scaleMaxM, IStringLocalizer? loc = null)
    {
        ArgumentNullException.ThrowIfNull(dph);
        ArgumentNullException.ThrowIfNull(werte);

        double height = Height(scaleMaxM);
        int maxN = werte.Count == 0 ? 0 : werte.Max(w => w.N10);
        int xMax = Math.Max(20, (int)Math.Ceiling(maxN / 5.0) * 5);
        double X(double n) => PlotX + n / xMax * PlotW;

        var sb = new StringBuilder();
        sb.Append($"<svg class=\"rammdiagramm\" viewBox=\"0 0 {N(Width)} {N(height)}\" xmlns=\"http://www.w3.org/2000/svg\" role=\"img\" aria-labelledby=\"rd-title rd-desc\" preserveAspectRatio=\"xMinYMin meet\">");
        sb.Append($"<title id=\"rd-title\">{Esc(Txt(loc, "Rammdiagramm {0}", dph.Bezeichnung))}</title>");
        sb.Append($"<desc id=\"rd-desc\">{Esc(Txt(loc, "Schlagzahlen N10 je 10 cm der schweren Rammsonde {0} bis {1} m Tiefe, höchster Wert {2}.", dph.Bezeichnung, Fmt(dph.EndteufeM, 2), maxN))}</desc>");

        Header(sb, Width, Txt(loc, "{0} · schwere Rammsonde (DIN EN ISO 22476-2)", dph.Bezeichnung),
            Txt(loc, "Endteufe {0} m · {1} · Ansatzhöhe {2} m NHN", Fmt(dph.EndteufeM, 2), dph.Datum.ToString("dd.MM.yyyy"), Fmt(dph.AnsatzhoeheMNhn, 2)));

        double y0 = Y(0), yBottom = Y(scaleMaxM);

        // grid: horizontal every 0.5 m, vertical every 5 blows
        for (double d = 0; d <= scaleMaxM + 1e-9; d += 0.5)
        {
            sb.Append($"<line class=\"dr-grid\" x1=\"{N(PlotX)}\" y1=\"{N(Y(d))}\" x2=\"{N(PlotX + PlotW)}\" y2=\"{N(Y(d))}\"/>");
            sb.Append($"<text class=\"dr-tick\" x=\"{N(PlotX - 8)}\" y=\"{N(Y(d) + 4)}\" text-anchor=\"end\">{Fmt(d, 1)}</text>");
        }

        sb.Append($"<text class=\"dr-colhead\" x=\"4\" y=\"{N(y0 - 16)}\">{Esc(Txt(loc, "m u. GOK"))}</text>");
        for (int n = 0; n <= xMax; n += 5)
        {
            sb.Append($"<line class=\"dr-grid\" x1=\"{N(X(n))}\" y1=\"{N(y0)}\" x2=\"{N(X(n))}\" y2=\"{N(yBottom)}\"/>");
            sb.Append($"<text class=\"dr-tick\" x=\"{N(X(n))}\" y=\"{N(yBottom + 16)}\" text-anchor=\"middle\">{n}</text>");
        }

        sb.Append($"<text class=\"dr-axislabel\" x=\"{N(PlotX + PlotW / 2)}\" y=\"{N(yBottom + 34)}\" text-anchor=\"middle\">{Esc(Txt(loc, "N10 (Schläge je 10 cm)"))}</text>");

        // bars: one per 10 cm step
        sb.Append("<g class=\"rd-bars\">");
        foreach (var w in werte.OrderBy(w => w.TiefeBisM))
        {
            double top = Y(w.TiefeBisM - 0.1);
            sb.Append($"<rect class=\"rd-bar\" data-tiefe=\"{N(w.TiefeBisM)}\" data-n10=\"{w.N10}\" x=\"{N(PlotX)}\" y=\"{N(top + 0.75)}\" width=\"{N(Math.Max(0, w.N10) / (double)xMax * PlotW)}\" height=\"{N(PlotWidthStep() - 1.5)}\"/>");
        }

        sb.Append("</g>");

        sb.Append($"<line class=\"dr-axis\" x1=\"{N(PlotX)}\" y1=\"{N(y0)}\" x2=\"{N(PlotX)}\" y2=\"{N(yBottom)}\"/>");
        double yEnd = Y(dph.EndteufeM);
        sb.Append($"<line class=\"dr-end\" x1=\"{N(PlotX - 10)}\" y1=\"{N(yEnd)}\" x2=\"{N(PlotX + PlotW)}\" y2=\"{N(yEnd)}\"/>");
        sb.Append("</svg>");
        return sb.ToString();
    }

    private static double PlotWidthStep() => 0.1 * PxPerM;

    /// <summary>German text when no localizer is passed (PDF report); otherwise the localized value (docs/i18n.md, rule (c)).</summary>
    private static string Txt(IStringLocalizer? loc, string key, params object[] args) =>
        loc is null ? string.Format(key, args) : loc[key, args].Value;
}
