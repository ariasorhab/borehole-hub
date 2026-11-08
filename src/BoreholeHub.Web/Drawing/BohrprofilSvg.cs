using System.Text;
using BoreholeHub.Core.Entities;
using static BoreholeHub.Web.Drawing.DrawingLayout;

namespace BoreholeHub.Web.Drawing;

/// <summary>
/// Borehole log ("Bohrprofil") as an inline SVG string, drawn in the spirit of DIN 4023 (simplified):
/// depth axis, layer column with soil hatches, absolute heights, descriptions with leader lines, groundwater and final depth.
/// Text and lines use currentColor or CSS classes (see aufschluss.css) so that the drawing works in both themes;
/// only the soil fills and hatch colours are fixed.
/// </summary>
public static class BohrprofilSvg
{
    private const double Width = 760;
    private const double AxisX = 52;
    private const double NhnRight = 114;
    private const double ColX = 124;
    private const double ColW = 84;
    private const double ColR = ColX + ColW;
    private const double TextX = 234;
    private const double LineH = 15;
    private const int WrapChars = 70;

    public static string Render(Aufschluss a, IReadOnlyList<Schicht> schichten, double scaleMaxM)
    {
        ArgumentNullException.ThrowIfNull(a);
        ArgumentNullException.ThrowIfNull(schichten);

        var layers = schichten.OrderBy(s => s.VonM).ToList();
        double height = Height(scaleMaxM);
        var sb = new StringBuilder();
        string title = $"Bohrprofil {a.Bezeichnung}";
        sb.Append($"<svg class=\"bohrprofil\" viewBox=\"0 0 {N(Width)} {N(height)}\" xmlns=\"http://www.w3.org/2000/svg\" role=\"img\" aria-labelledby=\"bp-title bp-desc\" preserveAspectRatio=\"xMinYMin meet\">");
        sb.Append($"<title id=\"bp-title\">{Esc(title)}</title>");
        sb.Append($"<desc id=\"bp-desc\">{Esc(Describe(a, layers))}</desc>");

        // ---- patterns (one per distinct soil combination) ----
        var kinds = layers.Select(SoilPatterns.Classify).ToList();
        sb.Append("<defs>");
        foreach (var key in kinds.Select(k => k.Key).Distinct())
        {
            sb.Append(SoilPatterns.Pattern(kinds.First(k => k.Key == key)));
        }

        sb.Append("</defs>");

        // ---- header ----
        string coords = $"R {Fmt(a.Rechtswert, 0)} / H {Fmt(a.Hochwert, 0)} (EPSG:25832)";
        Header(sb, Width, $"{a.Bezeichnung} · Kleinrammbohrung",
            $"Ansatzhöhe {Fmt(a.AnsatzhoeheMNhn, 2)} m NHN · {a.Datum:dd.MM.yyyy} · {coords}");

        double y0 = Y(0);
        double yEnd = Y(a.EndteufeM);

        // ---- depth axis ----
        sb.Append($"<text class=\"dr-colhead\" x=\"4\" y=\"{N(y0 - 16)}\">m u. GOK</text>");
        sb.Append($"<text class=\"dr-colhead\" x=\"{N(NhnRight)}\" y=\"{N(y0 - 16)}\" text-anchor=\"end\">m NHN</text>");
        sb.Append($"<line class=\"dr-axis\" x1=\"{N(AxisX)}\" y1=\"{N(y0)}\" x2=\"{N(AxisX)}\" y2=\"{N(Y(scaleMaxM))}\"/>");
        for (double d = 0; d <= scaleMaxM + 1e-9; d += 0.5)
        {
            double y = Y(d);
            bool whole = Math.Abs(d - Math.Round(d)) < 1e-9;
            sb.Append($"<line class=\"dr-axis\" x1=\"{N(AxisX - (whole ? 8 : 5))}\" y1=\"{N(y)}\" x2=\"{N(AxisX)}\" y2=\"{N(y)}\"/>");
            sb.Append($"<text class=\"dr-tick\" x=\"{N(AxisX - 12)}\" y=\"{N(y + 4)}\" text-anchor=\"end\">{Fmt(d, 1)}</text>");
        }

        // ---- layer column ----
        sb.Append("<g class=\"bp-layers\">");
        for (int i = 0; i < layers.Count; i++)
        {
            var s = layers[i];
            double ya = Y(s.VonM), yb = Y(s.BisM);
            sb.Append($"<rect class=\"bp-layer\" data-von=\"{N(s.VonM)}\" data-bis=\"{N(s.BisM)}\" x=\"{N(ColX)}\" y=\"{N(ya)}\" width=\"{N(ColW)}\" height=\"{N(yb - ya)}\" fill=\"url(#p-{kinds[i].Key})\"/>");
        }

        sb.Append("</g>");

        // boundaries and their absolute heights
        var bounds = layers.Select(s => s.VonM).Concat(layers.Select(s => s.BisM)).Append(0).Append(a.EndteufeM)
            .Select(v => Math.Round(v, 2)).Distinct().OrderBy(v => v).ToList();
        foreach (double b in bounds)
        {
            double y = Y(b);
            sb.Append($"<line class=\"dr-line\" x1=\"{N(ColX)}\" y1=\"{N(y)}\" x2=\"{N(ColR)}\" y2=\"{N(y)}\"/>");
            sb.Append($"<text class=\"dr-nhn\" x=\"{N(NhnRight)}\" y=\"{N(y + 4)}\" text-anchor=\"end\">{Fmt(a.AnsatzhoeheMNhn - b, 2)}</text>");
        }

        sb.Append($"<rect class=\"dr-frame\" x=\"{N(ColX)}\" y=\"{N(y0)}\" width=\"{N(ColW)}\" height=\"{N(yEnd - y0)}\"/>");

        // ---- final depth ----
        sb.Append($"<line class=\"dr-end\" x1=\"{N(ColX - 4)}\" y1=\"{N(yEnd)}\" x2=\"{N(ColR + 10)}\" y2=\"{N(yEnd)}\"/>");
        sb.Append($"<text class=\"dr-et\" x=\"{N(ColX + ColW / 2)}\" y=\"{N(yEnd + 18)}\" text-anchor=\"middle\">ET {Fmt(a.EndteufeM, 2)} m</text>");

        // ---- text blocks (layers and groundwater) with overlap resolution and leader lines ----
        var blocks = new List<Block>();
        for (int i = 0; i < layers.Count; i++)
        {
            var s = layers[i];
            blocks.Add(new Block(Y((s.VonM + s.BisM) / 2), LayerLines(s), false));
        }

        if (a.GrundwasserM is double gw)
        {
            string gwText = $"GW {Fmt(gw, 2)} m ({a.Datum:dd.MM.yyyy})";
            blocks.Add(new Block(Y(gw), [new Line([new Run(gwText, "dr-gw-text")])], true));
        }

        Place(blocks, y0 - 2, Y(scaleMaxM) + BottomPad - 14);

        foreach (var b in blocks)
        {
            double ym = b.DesiredY;
            double first = b.Top + 11;
            string cls = b.IsWater ? "dr-leader dr-leader-gw" : "dr-leader";
            if (Math.Abs(first - ym) < 2.5)
            {
                sb.Append($"<line class=\"{cls}\" x1=\"{N(ColR)}\" y1=\"{N(ym)}\" x2=\"{N(TextX - 6)}\" y2=\"{N(ym)}\"/>");
            }
            else
            {
                sb.Append($"<polyline class=\"{cls}\" points=\"{N(ColR)},{N(ym)} {N(ColR + 12)},{N(ym)} {N(TextX - 14)},{N(first)} {N(TextX - 6)},{N(first)}\"/>");
            }

            sb.Append($"<text class=\"dr-block\" x=\"{N(TextX)}\">");
            for (int i = 0; i < b.Lines.Count; i++)
            {
                sb.Append($"<tspan x=\"{N(TextX)}\" y=\"{N(b.Top + 11 + i * LineH)}\">");
                foreach (var run in b.Lines[i].Runs)
                {
                    sb.Append(run.Css is null ? Esc(run.Text) : $"<tspan class=\"{run.Css}\">{Esc(run.Text)}</tspan>");
                }

                sb.Append("</tspan>");
            }

            sb.Append("</text>");
        }

        // ---- groundwater symbol in the column ----
        if (a.GrundwasserM is double gwm)
        {
            double y = Y(gwm);
            double cx = ColX + 16;
            sb.Append($"<line class=\"dr-gw-line\" x1=\"{N(ColX - 6)}\" y1=\"{N(y)}\" x2=\"{N(ColR)}\" y2=\"{N(y)}\"/>");
            sb.Append($"<path class=\"dr-gw-sym\" d=\"M{N(cx - 9)},{N(y - 14)} L{N(cx + 9)},{N(y - 14)} L{N(cx)},{N(y)} Z\"/>");
            sb.Append($"<line class=\"dr-gw-sym-line\" x1=\"{N(cx - 12)}\" y1=\"{N(y - 14)}\" x2=\"{N(cx + 12)}\" y2=\"{N(y - 14)}\"/>");
        }

        sb.Append("</svg>");
        return sb.ToString();
    }

    // ---------- text blocks ----------

    private record Run(string Text, string? Css = null);

    private record Line(List<Run> Runs);

    private sealed class Block(double desiredY, List<Line> lines, bool isWater)
    {
        public double DesiredY { get; } = desiredY;
        public List<Line> Lines { get; } = lines;
        public bool IsWater { get; } = isWater;
        public double Height => Lines.Count * LineH;
        public double Top { get; set; }
    }

    private static List<Line> LayerLines(Schicht s)
    {
        var lines = new List<Line>();
        var head = new List<Run> { new(s.Kurzzeichen, "dr-bold") };
        if (!string.IsNullOrWhiteSpace(s.Bodengruppe))
        {
            head.Add(new Run($"  ({s.Bodengruppe})", "dr-muted"));
        }

        lines.Add(new Line(head));
        foreach (var part in Wrap(s.Beschreibung, WrapChars))
        {
            lines.Add(new Line([new Run(part)]));
        }

        var extra = new[] { s.Zustand, s.Farbe }.Where(x => !string.IsNullOrWhiteSpace(x)).ToList();
        if (extra.Count > 0)
        {
            lines.Add(new Line([new Run(string.Join(" · ", extra), "dr-muted")]));
        }

        return lines;
    }

    private static List<string> Wrap(string text, int max)
    {
        var result = new List<string>();
        var current = new StringBuilder();
        foreach (var word in (text ?? string.Empty).Split(' ', StringSplitOptions.RemoveEmptyEntries))
        {
            if (current.Length > 0 && current.Length + 1 + word.Length > max)
            {
                result.Add(current.ToString());
                current.Clear();
            }

            if (current.Length > 0) { current.Append(' '); }
            current.Append(word);
        }

        if (current.Length > 0) { result.Add(current.ToString()); }
        return result;
    }

    /// <summary>Centres each block on its layer, then pushes blocks down (and, at the bottom, back up) until none overlap.</summary>
    private static void Place(List<Block> blocks, double minTop, double maxBottom)
    {
        blocks.Sort((p, q) => p.DesiredY.CompareTo(q.DesiredY));
        const double gap = 5;
        double previousBottom = minTop;
        foreach (var b in blocks)
        {
            b.Top = Math.Max(b.DesiredY - b.Height / 2, previousBottom + (previousBottom > minTop ? gap : 0));
            previousBottom = b.Top + b.Height;
        }

        double limit = maxBottom;
        for (int i = blocks.Count - 1; i >= 0; i--)
        {
            var b = blocks[i];
            if (b.Top + b.Height > limit)
            {
                b.Top = limit - b.Height;
            }

            limit = b.Top - gap;
        }
    }

    private static string Describe(Aufschluss a, List<Schicht> layers)
    {
        var parts = layers.Select(s => $"{Fmt(s.VonM, 2)} bis {Fmt(s.BisM, 2)} m: {s.Beschreibung}");
        string gw = a.GrundwasserM is double g ? $" Grundwasser bei {Fmt(g, 2)} m u. GOK." : " Grundwasser nicht angetroffen.";
        return $"Schichtenprofil der Kleinrammbohrung {a.Bezeichnung}, Endteufe {Fmt(a.EndteufeM, 2)} m. " + string.Join("; ", parts) + "." + gw;
    }
}
