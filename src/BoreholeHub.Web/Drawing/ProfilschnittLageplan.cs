using System.Text;
using BoreholeHub.Core.Entities;
using static BoreholeHub.Web.Drawing.DrawingLayout;

namespace BoreholeHub.Web.Drawing;

/// <summary>Tiny inset plan for the section page: all Aufschlüsse of the project and the section line (north is up).</summary>
internal static class ProfilschnittLageplan
{
    private const double W = 260;
    private const double H = 210;
    private const double Pad = 30;

    public static string Render(IReadOnlyList<Aufschluss> alle, IReadOnlyList<Aufschluss> schnitt)
    {
        ArgumentNullException.ThrowIfNull(alle);
        ArgumentNullException.ThrowIfNull(schnitt);

        var sb = new StringBuilder();
        sb.Append($"<svg class=\"ps-plan\" viewBox=\"0 0 {N(W)} {N(H)}\" xmlns=\"http://www.w3.org/2000/svg\" role=\"img\" aria-labelledby=\"pl-title\">");
        sb.Append("<title id=\"pl-title\">Lageplan der Aufschlüsse mit Schnittlinie</title>");
        if (alle.Count == 0)
        {
            sb.Append("</svg>");
            return sb.ToString();
        }

        double minX = alle.Min(a => a.Rechtswert), maxX = alle.Max(a => a.Rechtswert);
        double minY = alle.Min(a => a.Hochwert), maxY = alle.Max(a => a.Hochwert);
        double spanX = Math.Max(1, maxX - minX), spanY = Math.Max(1, maxY - minY);
        double scale = Math.Min((W - 2 * Pad) / spanX, (H - 2 * Pad - 14) / spanY);
        double offX = (W - spanX * scale) / 2, offY = 14 + (H - 14 - spanY * scale) / 2;
        double X(double r) => offX + (r - minX) * scale;
        double Y(double h) => offY + (maxY - h) * scale;

        sb.Append($"<rect class=\"pl-frame\" x=\"0.5\" y=\"0.5\" width=\"{N(W - 1)}\" height=\"{N(H - 1)}\" rx=\"8\"/>");
        sb.Append($"<path class=\"pl-north\" d=\"M{N(W - 20)},{N(30)} L{N(W - 14)},{N(18)} L{N(W - 8)},{N(30)} L{N(W - 14)},{N(26)} Z\"/>");
        sb.Append($"<text class=\"pl-north-text\" x=\"{N(W - 14)}\" y=\"{N(14)}\" text-anchor=\"middle\">N</text>");

        if (schnitt.Count >= 2)
        {
            string line = string.Join(" ", schnitt.Select(a => $"{N(X(a.Rechtswert))},{N(Y(a.Hochwert))}"));
            sb.Append($"<polyline class=\"pl-line\" points=\"{line}\"/>");
        }

        var sel = schnitt.Select(a => a.Id).ToHashSet();
        foreach (var a in alle.OrderBy(a => sel.Contains(a.Id)))
        {
            double x = X(a.Rechtswert), y = Y(a.Hochwert);
            bool chosen = sel.Contains(a.Id);
            if (a.Art == AufschlussArt.Kleinrammbohrung)
            {
                sb.Append($"<circle class=\"pl-krb{(chosen ? " pl-sel" : string.Empty)}\" cx=\"{N(x)}\" cy=\"{N(y)}\" r=\"{(chosen ? 6 : 5)}\"/>");
                sb.Append($"<text class=\"pl-label{(chosen ? " pl-label-sel" : string.Empty)}\" x=\"{N(x)}\" y=\"{N(y - 10)}\" text-anchor=\"middle\">{Esc(a.Bezeichnung.Replace("KRB ", string.Empty))}</text>");
            }
            else
            {
                sb.Append($"<rect class=\"pl-dph\" x=\"{N(x - 3.5)}\" y=\"{N(y - 3.5)}\" width=\"7\" height=\"7\" transform=\"rotate(45 {N(x)} {N(y)})\"/>");
            }
        }

        sb.Append($"<text class=\"pl-note\" x=\"10\" y=\"{N(H - 8)}\">● KRB (Nr.)   ◆ DPH   — Schnitt</text>");
        sb.Append("</svg>");
        return sb.ToString();
    }
}
