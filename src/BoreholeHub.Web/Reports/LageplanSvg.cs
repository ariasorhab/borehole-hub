using System.Text;
using BoreholeHub.Core.Entities;
using static BoreholeHub.Web.Drawing.DrawingLayout;

namespace BoreholeHub.Web.Reports;

/// <summary>
/// Schematic site plan ("Lageplan") of all Aufschlüsse as SVG for the PDF report: points with labels, coordinate grid in EPSG:25832,
/// scale bar, north arrow and the line of the cross-section. No map tiles. All colours are fixed print colours (no CSS needed).
/// </summary>
internal static class LageplanSvg
{
    private const double W = 720;
    private const double H = 640;
    private const double PlotL = 84, PlotT = 20, PlotR = 696, PlotB = 560;
    private const string Ink = "#22201c", Muted = "#6b645a", Line = "#d9d2c3", Rust = "#b85a2e", Water = "#2d6a86";

    public static string Render(IReadOnlyList<Aufschluss> alle, IReadOnlyList<Aufschluss> schnitt)
    {
        ArgumentNullException.ThrowIfNull(alle);
        ArgumentNullException.ThrowIfNull(schnitt);

        var sb = new StringBuilder();
        sb.Append($"<svg viewBox=\"0 0 {N(W)} {N(H)}\" xmlns=\"http://www.w3.org/2000/svg\" font-family=\"Inter, Arial, sans-serif\" role=\"img\"><title>Lageplan der Aufschlüsse</title>");
        sb.Append($"<rect x=\"{N(PlotL)}\" y=\"{N(PlotT)}\" width=\"{N(PlotR - PlotL)}\" height=\"{N(PlotB - PlotT)}\" fill=\"#fdfcf9\" stroke=\"{Ink}\" stroke-width=\"1.2\"/>");
        if (alle.Count == 0)
        {
            sb.Append("</svg>");
            return sb.ToString();
        }

        double minX = alle.Min(a => a.Rechtswert), maxX = alle.Max(a => a.Rechtswert);
        double minY = alle.Min(a => a.Hochwert), maxY = alle.Max(a => a.Hochwert);
        double padX = Math.Max(5, (maxX - minX) * 0.18), padY = Math.Max(5, (maxY - minY) * 0.18);
        double x0 = minX - padX, x1 = maxX + padX, y0 = minY - padY, y1 = maxY + padY;
        double pw = PlotR - PlotL, ph = PlotB - PlotT;
        double scale = Math.Min(pw / (x1 - x0), ph / (y1 - y0));
        double cx = (x0 + x1) / 2, cy = (y0 + y1) / 2;
        double X(double r) => (PlotL + PlotR) / 2 + (r - cx) * scale;
        double Y(double h) => (PlotT + PlotB) / 2 - (h - cy) * scale;
        double visX0 = cx - pw / 2 / scale, visX1 = cx + pw / 2 / scale, visY0 = cy - ph / 2 / scale, visY1 = cy + ph / 2 / scale;

        // ---- coordinate grid ----
        double step = Nice(Math.Max(visX1 - visX0, visY1 - visY0) / 6);
        for (double e = Math.Ceiling(visX0 / step) * step; e <= visX1 + 1e-9; e += step)
        {
            sb.Append($"<line x1=\"{N(X(e))}\" y1=\"{N(PlotT)}\" x2=\"{N(X(e))}\" y2=\"{N(PlotB)}\" stroke=\"{Line}\" stroke-width=\"1\"/>");
            sb.Append($"<text x=\"{N(X(e))}\" y=\"{N(PlotB + 16)}\" font-size=\"11\" fill=\"{Muted}\" text-anchor=\"middle\">{e.ToString("0", De)}</text>");
        }

        for (double nn = Math.Ceiling(visY0 / step) * step; nn <= visY1 + 1e-9; nn += step)
        {
            sb.Append($"<line x1=\"{N(PlotL)}\" y1=\"{N(Y(nn))}\" x2=\"{N(PlotR)}\" y2=\"{N(Y(nn))}\" stroke=\"{Line}\" stroke-width=\"1\"/>");
            sb.Append($"<text x=\"{N(PlotL - 8)}\" y=\"{N(Y(nn) + 4)}\" font-size=\"11\" fill=\"{Muted}\" text-anchor=\"end\">{nn.ToString("0", De)}</text>");
        }

        sb.Append($"<text x=\"{N((PlotL + PlotR) / 2)}\" y=\"{N(PlotB + 36)}\" font-size=\"12\" fill=\"{Muted}\" text-anchor=\"middle\">Rechtswert [m], ETRS89 / UTM 32N (EPSG:25832)</text>");
        sb.Append($"<text x=\"16\" y=\"{N((PlotT + PlotB) / 2)}\" font-size=\"12\" fill=\"{Muted}\" text-anchor=\"middle\" transform=\"rotate(-90 16 {N((PlotT + PlotB) / 2)})\">Hochwert [m]</text>");

        // ---- section line ----
        if (schnitt.Count >= 2)
        {
            string pts = string.Join(" ", schnitt.Select(a => $"{N(X(a.Rechtswert))},{N(Y(a.Hochwert))}"));
            sb.Append($"<polyline points=\"{pts}\" fill=\"none\" stroke=\"{Rust}\" stroke-width=\"2.2\" stroke-dasharray=\"9 5\"/>");
        }

        // ---- points ----
        foreach (var a in alle.OrderBy(a => a.Art).ThenBy(a => a.Id))
        {
            double x = X(a.Rechtswert), y = Y(a.Hochwert);
            if (a.Art == AufschlussArt.Kleinrammbohrung)
            {
                if (a.GrundwasserM is not null)
                {
                    sb.Append($"<circle cx=\"{N(x)}\" cy=\"{N(y)}\" r=\"10\" fill=\"none\" stroke=\"{Water}\" stroke-width=\"2\"/>");
                }

                sb.Append($"<circle cx=\"{N(x)}\" cy=\"{N(y)}\" r=\"6\" fill=\"{Rust}\" stroke=\"#ffffff\" stroke-width=\"1.5\"/>");
                Label(sb, a.Bezeichnung, x - 12, y - 12, "end", Ink, true);
            }
            else
            {
                sb.Append($"<rect x=\"{N(x - 5)}\" y=\"{N(y - 5)}\" width=\"10\" height=\"10\" fill=\"{Water}\" stroke=\"#ffffff\" stroke-width=\"1.5\" transform=\"rotate(45 {N(x)} {N(y)})\"/>");
                Label(sb, a.Bezeichnung, x + 11, y + 18, "start", Water, false);
            }
        }

        // ---- north arrow ----
        double nx = PlotR - 30, ny = PlotT + 56;
        sb.Append($"<path d=\"M{N(nx)},{N(ny - 36)} L{N(nx + 10)},{N(ny)} L{N(nx)},{N(ny - 8)} L{N(nx - 10)},{N(ny)} Z\" fill=\"{Ink}\"/>");
        sb.Append($"<text x=\"{N(nx)}\" y=\"{N(ny - 42)}\" font-size=\"14\" font-weight=\"600\" fill=\"{Ink}\" text-anchor=\"middle\">N</text>");

        // ---- scale bar (length = a round number of metres, about a quarter of the plot width) ----
        double barM = Nice(pw / scale / 4);
        double barPx = barM * scale;
        double bx = PlotL + 22, by = PlotB - 26;
        sb.Append($"<rect x=\"{N(bx - 8)}\" y=\"{N(by - 22)}\" width=\"{N(barPx + 70)}\" height=\"46\" fill=\"#ffffff\" fill-opacity=\"0.85\"/>");
        for (int i = 0; i < 4; i++)
        {
            sb.Append($"<rect x=\"{N(bx + i * barPx / 4)}\" y=\"{N(by)}\" width=\"{N(barPx / 4)}\" height=\"7\" fill=\"{(i % 2 == 0 ? Ink : "#ffffff")}\" stroke=\"{Ink}\" stroke-width=\"1\"/>");
        }

        sb.Append($"<text x=\"{N(bx)}\" y=\"{N(by - 5)}\" font-size=\"11\" fill=\"{Ink}\" text-anchor=\"middle\">0</text>");
        sb.Append($"<text x=\"{N(bx + barPx)}\" y=\"{N(by - 5)}\" font-size=\"11\" fill=\"{Ink}\" text-anchor=\"middle\">{barM.ToString("0", De)} m</text>");
        sb.Append($"<text x=\"{N(bx)}\" y=\"{N(by + 20)}\" font-size=\"10.5\" fill=\"{Muted}\">Maßstabsbalken (schematisch, ohne Kartengrundlage)</text>");

        // ---- legend ----
        double ly = PlotB + 54, lx = PlotL;
        sb.Append($"<circle cx=\"{N(lx + 6)}\" cy=\"{N(ly)}\" r=\"6\" fill=\"{Rust}\"/><text x=\"{N(lx + 18)}\" y=\"{N(ly + 4)}\" font-size=\"12\" fill=\"{Ink}\">Kleinrammbohrung (KRB)</text>");
        lx += 196;
        sb.Append($"<rect x=\"{N(lx + 1)}\" y=\"{N(ly - 5)}\" width=\"10\" height=\"10\" fill=\"{Water}\" transform=\"rotate(45 {N(lx + 6)} {N(ly)})\"/><text x=\"{N(lx + 18)}\" y=\"{N(ly + 4)}\" font-size=\"12\" fill=\"{Ink}\">Rammsondierung (DPH)</text>");
        lx += 186;
        sb.Append($"<circle cx=\"{N(lx + 6)}\" cy=\"{N(ly)}\" r=\"9\" fill=\"none\" stroke=\"{Water}\" stroke-width=\"2\"/><circle cx=\"{N(lx + 6)}\" cy=\"{N(ly)}\" r=\"5\" fill=\"{Rust}\"/><text x=\"{N(lx + 21)}\" y=\"{N(ly + 4)}\" font-size=\"12\" fill=\"{Ink}\">Grundwasser angetroffen</text>");
        if (schnitt.Count >= 2)
        {
            sb.Append($"<line x1=\"{N(PlotL)}\" y1=\"{N(ly + 24)}\" x2=\"{N(PlotL + 28)}\" y2=\"{N(ly + 24)}\" stroke=\"{Rust}\" stroke-width=\"2.2\" stroke-dasharray=\"7 4\"/><text x=\"{N(PlotL + 38)}\" y=\"{N(ly + 28)}\" font-size=\"12\" fill=\"{Ink}\">Lage des Profilschnitts (Anlage 3)</text>");
        }

        sb.Append("</svg>");
        return sb.ToString();
    }

    private static void Label(StringBuilder sb, string text, double x, double y, string anchor, string colour, bool bold)
    {
        double w = text.Length * 7.2 + 8;
        double rx = anchor == "end" ? x - w + 4 : x - 4;
        sb.Append($"<rect x=\"{N(rx)}\" y=\"{N(y - 12)}\" width=\"{N(w)}\" height=\"17\" rx=\"3\" fill=\"#ffffff\" fill-opacity=\"0.82\"/>");
        sb.Append($"<text x=\"{N(x)}\" y=\"{N(y)}\" font-size=\"12.5\" font-weight=\"{(bold ? 600 : 400)}\" fill=\"{colour}\" text-anchor=\"{anchor}\">{Esc(text)}</text>");
    }

    private static double Nice(double v)
    {
        double p = Math.Pow(10, Math.Floor(Math.Log10(Math.Max(v, 1e-6))));
        foreach (double m in new[] { 1.0, 2, 5, 10 })
        {
            if (m * p >= v - 1e-9) { return m * p; }
        }

        return 10 * p;
    }
}
