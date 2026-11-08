using System.Globalization;
using System.Text;
using System.Text.RegularExpressions;

namespace BoreholeHub.Web.Reports;

/// <summary>
/// The web drawings colour their text and lines with CSS classes and theme tokens (see aufschluss.css, profilschnitt.css).
/// The PDF renderer (Skia SVG) ignores style sheets, so for the PDF every class is replaced in a copy of the SVG string by an
/// inline style attribute with fixed print colours. The web output is not touched.
/// </summary>
public static class SvgPrint
{
    private const string Ink = "#22201c", Muted = "#6b645a", Line = "#e3ddd0", Water = "#2d6a86", Rust = "#b85a2e", Sand = "#f3efe6", White = "#ffffff";

    // No paint-order halos, no currentColor, no var().
    private static readonly Dictionary<string, string> Classes = new(StringComparer.Ordinal)
    {
        ["dr-head"] = "fill: "+Sand+"; stroke: "+Line+"; stroke-width: 1;",
        ["dr-title"] = "font-size: 17px; font-weight: 600; fill: "+Ink+";",
        ["dr-sub"] = "font-size: 12.5px; fill: "+Muted+";",
        ["dr-colhead"] = "font-size: 11px; fill: "+Muted+";",
        ["dr-tick"] = "font-size: 11px; fill: "+Muted+";",
        ["dr-nhn"] = "font-size: 11px; fill: "+Muted+";",
        ["dr-axislabel"] = "font-size: 12px; fill: "+Muted+";",
        ["dr-axis"] = "stroke: "+Muted+"; stroke-width: 1;",
        ["dr-grid"] = "stroke: "+Line+"; stroke-width: 1;",
        ["dr-line"] = "stroke: "+Ink+"; stroke-width: 1; stroke-opacity: 0.75;",
        ["dr-frame"] = "fill: none; stroke: "+Ink+"; stroke-width: 1.6;",
        ["dr-end"] = "stroke: "+Ink+"; stroke-width: 2.4;",
        ["dr-et"] = "font-size: 12px; font-weight: 600; fill: "+Ink+";",
        ["dr-block"] = "font-size: 12.5px; fill: "+Ink+";",
        ["dr-bold"] = "font-weight: 600;",
        ["dr-muted"] = "fill: "+Muted+";",
        ["dr-leader"] = "fill: none; stroke: "+Muted+"; stroke-width: 1;",
        ["dr-leader-gw"] = "stroke: "+Water+";",
        ["dr-gw-line"] = "stroke: "+Water+"; stroke-width: 2; stroke-dasharray: 6 3;",
        ["dr-gw-sym"] = "fill: "+Water+"; stroke: "+White+"; stroke-width: 1.5;",
        ["dr-gw-sym-line"] = "stroke: "+Water+"; stroke-width: 2;",
        ["dr-gw-text"] = "fill: "+Water+"; font-weight: 600;",
        ["rd-bar"] = "fill: "+Rust+";",
        ["ps-grid"] = "stroke: "+Line+"; stroke-width: 1;",
        ["ps-axis"] = "stroke: "+Muted+"; stroke-width: 1;",
        ["ps-tick"] = "font-size: 11px; fill: "+Muted+";",
        ["ps-axislabel"] = "font-size: 12px; fill: "+Muted+";",
        ["ps-station"] = "font-size: 11.5px; font-weight: 600; fill: "+Ink+";",
        ["ps-station-mark"] = "fill: "+Ink+";",
        ["ps-col-name"] = "font-size: 13px; font-weight: 600; fill: "+Ink+";",
        ["ps-col-sub"] = "font-size: 11px; fill: "+Muted+";",
        ["ps-leader"] = "stroke: "+Muted+"; stroke-width: 1; stroke-dasharray: 2 3;",
        ["ps-body"] = "stroke: none; opacity: 0.55;",
        ["ps-interp"] = "fill: none; stroke: "+Ink+"; stroke-width: 1; stroke-dasharray: 5 3; stroke-opacity: 0.55;",
        ["ps-ground"] = "fill: none; stroke: "+Ink+"; stroke-width: 2.4;",
        ["ps-ground-ext"] = "stroke: "+Ink+"; stroke-width: 1.4; stroke-dasharray: 3 4; stroke-opacity: 0.6;",
        ["ps-frame"] = "fill: none; stroke: "+Ink+"; stroke-width: 1.6;",
        ["ps-colline"] = "stroke: "+Ink+"; stroke-width: 1; stroke-opacity: 0.75;",
        ["ps-et"] = "stroke: "+Ink+"; stroke-width: 3;",
        ["ps-et-text"] = "font-size: 11px; font-weight: 600; fill: "+Ink+";",
        ["ps-hb"] = "fill: "+White+"; stroke: "+Muted+"; stroke-width: 1;",
        ["ps-hb-text"] = "font-size: 11px; font-weight: 600; fill: "+Ink+";",
        ["ps-gw-line"] = "fill: none; stroke: "+Water+"; stroke-width: 2.2; stroke-dasharray: 7 4;",
        ["ps-gw-sym"] = "fill: "+Water+"; stroke: "+White+"; stroke-width: 1.5;",
        ["ps-gw-sym-line"] = "stroke: "+Water+"; stroke-width: 2;",
        ["ps-gw-text"] = "font-size: 11px; font-weight: 600; fill: "+Water+";",
        ["ps-swatch"] = "stroke: "+Ink+"; stroke-width: 1; stroke-opacity: 0.6;",
        ["ps-legend-text"] = "font-size: 12px; fill: "+Ink+";",
        ["ps-caption"] = "font-size: 12.5px; fill: "+Muted+";",
        ["ps-caption-strong"] = "fill: "+Ink+"; font-weight: 600;",
    };

    private static readonly Regex ClassAttribute = new("(<[A-Za-z]+\\b[^>]*?)\\sclass=\"([^\"]*)\"", RegexOptions.Compiled);

    private static readonly Regex PatternDef = new(
        "<pattern id=\"(?<id>[^\"]+)\" width=\"16\" height=\"16\" patternUnits=\"userSpaceOnUse\"><rect width=\"16\" height=\"16\" fill=\"(?<bg>[^\"]+)\"/>(?<rest>.*?)</pattern>",
        RegexOptions.Compiled | RegexOptions.Singleline);

    private static readonly Regex PatternUse = new(
        "<(?<tag>rect|polygon)\\s(?<attrs>[^>]*?fill=\"url\\(#(?<id>[^\"]+)\\)\"[^>]*?)/>", RegexOptions.Compiled);

    /// <summary>
    /// Returns a copy of <paramref name="svg"/> for the PDF: each class attribute becomes an inline style attribute, and every
    /// hatch pattern fill becomes vector tiles (the PDF renderer would rasterise SVG patterns at 72 dpi, which looks blurry and bloats the file).
    /// </summary>
    public static string Apply(string svg)
    {
        string styled = ClassAttribute.Replace(svg, m =>
        {
            var declarations = m.Groups[2].Value.Split(' ', StringSplitOptions.RemoveEmptyEntries)
                .Select(c => Classes.TryGetValue(c, out var d) ? d : null)
                .Where(d => d is not null);
            string style = string.Join(" ", declarations);
            return style.Length == 0 ? m.Groups[1].Value : $"{m.Groups[1].Value} style=\"{style}\"";
        });
        return ExpandPatterns(styled);
    }

    private static string ExpandPatterns(string svg)
    {
        var backgrounds = new Dictionary<string, string>();
        string result = PatternDef.Replace(svg, m =>
        {
            backgrounds[m.Groups["id"].Value] = m.Groups["bg"].Value;
            return $"<g id=\"{m.Groups["id"].Value}-tile\">{m.Groups["rest"].Value}</g>";
        });
        if (backgrounds.Count == 0)
        {
            return result;
        }

        int counter = 0;
        return PatternUse.Replace(result, m =>
        {
            string id = m.Groups["id"].Value;
            if (!backgrounds.TryGetValue(id, out var bg)) { return m.Value; }

            string attrs = m.Groups["attrs"].Value;
            string tag = m.Groups["tag"].Value;
            double minX, minY, maxX, maxY;
            string clip;
            if (tag == "rect")
            {
                double x = Num(attrs, "x"), y = Num(attrs, "y"), w = Num(attrs, "width"), h = Num(attrs, "height");
                (minX, minY, maxX, maxY) = (x, y, x + w, y + h);
                clip = $"<rect x=\"{Fmt(x)}\" y=\"{Fmt(y)}\" width=\"{Fmt(w)}\" height=\"{Fmt(h)}\"/>";
            }
            else
            {
                string points = Regex.Match(attrs, "points=\"([^\"]+)\"").Groups[1].Value;
                var xy = points.Split(new[] { ' ', ',' }, StringSplitOptions.RemoveEmptyEntries).Select(v => double.Parse(v, CultureInfo.InvariantCulture)).ToArray();
                var xs = xy.Where((_, i) => i % 2 == 0).ToArray();
                var ys = xy.Where((_, i) => i % 2 == 1).ToArray();
                (minX, minY, maxX, maxY) = (xs.Min(), ys.Min(), xs.Max(), ys.Max());
                clip = $"<polygon points=\"{points}\"/>";
            }

            var opacity = Regex.Match(attrs, "opacity:\\s*([0-9.]+)");
            string group = opacity.Success ? $" style=\"opacity: {opacity.Groups[1].Value}\"" : string.Empty;
            string clipId = $"pc{++counter}";
            var sb = new StringBuilder();
            sb.Append($"<clipPath id=\"{clipId}\">{clip}</clipPath>");
            sb.Append($"<g clip-path=\"url(#{clipId})\"{group}>");
            sb.Append($"<rect x=\"{Fmt(minX)}\" y=\"{Fmt(minY)}\" width=\"{Fmt(maxX - minX)}\" height=\"{Fmt(maxY - minY)}\" fill=\"{bg}\"/>");
            for (double ty = Math.Floor(minY / 16) * 16 - 16; ty <= maxY; ty += 16)
            {
                for (double tx = Math.Floor(minX / 16) * 16 - 16; tx <= maxX; tx += 16)
                {
                    sb.Append($"<use href=\"#{id}-tile\" x=\"{Fmt(tx)}\" y=\"{Fmt(ty)}\"/>");
                }
            }

            sb.Append("</g>");
            // outline only (stroke from the original style, if any)
            string outline = m.Value.Replace($"fill=\"url(#{id})\"", "fill=\"none\"");
            sb.Append(outline);
            return sb.ToString();
        });
    }

    private static double Num(string attrs, string name) =>
        double.Parse(Regex.Match(attrs, $"(?<![\\w-]){name}=\"([^\"]+)\"").Groups[1].Value, CultureInfo.InvariantCulture);

    private static string Fmt(double v) => Math.Round(v, 2).ToString("0.##", CultureInfo.InvariantCulture);
}
