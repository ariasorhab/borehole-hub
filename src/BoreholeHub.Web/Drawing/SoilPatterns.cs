using System.Text;
using BoreholeHub.Core.Entities;

namespace BoreholeHub.Web.Drawing;

/// <summary>Soil classification and hatch patterns shared by the borehole log and the cross-section.</summary>
internal static class SoilPatterns
{
    // ---------- soil classification and hatches ----------

    internal enum Soil { Kies, Sand, Schluff, Ton, Oberboden, Unbekannt }

    internal sealed record SoilKind(Soil Main, char Second, bool Fill)
    {
        public string Key => $"{Main}{(Second == '\0' ? "" : "-" + Second)}{(Fill ? "-A" : "")}";
    }

    internal static SoilKind Classify(Schicht s)
    {
        var tokens = (s.Kurzzeichen ?? string.Empty).Split(',', StringSplitOptions.TrimEntries | StringSplitOptions.RemoveEmptyEntries)
            .Select(t => t.Replace("'", string.Empty).Replace("’", string.Empty)).ToList();
        bool fill = tokens.Contains("A") || (s.Beschreibung ?? string.Empty).StartsWith("Auffüllung", StringComparison.OrdinalIgnoreCase);
        bool top = string.Equals(s.Bodengruppe, "OH", StringComparison.OrdinalIgnoreCase)
            || (s.Beschreibung ?? string.Empty).StartsWith("Mutterboden", StringComparison.OrdinalIgnoreCase)
            || (s.Beschreibung ?? string.Empty).StartsWith("Oberboden", StringComparison.OrdinalIgnoreCase);

        Soil main = Soil.Unbekannt;
        int mainIndex = -1;
        for (int i = 0; i < tokens.Count; i++)
        {
            var m = tokens[i] switch { "G" => Soil.Kies, "S" => Soil.Sand, "U" => Soil.Schluff, "T" => Soil.Ton, _ => Soil.Unbekannt };
            if (m != Soil.Unbekannt) { main = m; mainIndex = i; break; }
        }

        if (main == Soil.Unbekannt && !string.IsNullOrEmpty(s.Bodengruppe))
        {
            main = char.ToUpperInvariant(s.Bodengruppe[0]) switch { 'G' => Soil.Kies, 'S' => Soil.Sand, 'U' => Soil.Schluff, 'T' => Soil.Ton, 'O' => Soil.Oberboden, _ => Soil.Unbekannt };
        }

        if (top) { main = Soil.Oberboden; }

        char second = '\0';
        for (int i = mainIndex + 1; i < tokens.Count && mainIndex >= 0 && !top; i++)
        {
            if (tokens[i].Length == 1 && "sgut".Contains(tokens[i][0]))
            {
                second = tokens[i][0];
                break;
            }
        }

        return new SoilKind(main, second, fill && !top);
    }

    internal static (string Bg, string Mark) Colours(Soil soil) => soil switch
    {
        Soil.Kies => ("#e3cd96", "#6d5a2a"),
        Soil.Sand => ("#f1e3a8", "#85702a"),
        Soil.Schluff => ("#d8cbb0", "#6b5e46"),
        Soil.Ton => ("#bccbb3", "#46583e"),
        Soil.Oberboden => ("#b79c82", "#3e2e20"),
        _ => ("#d9d6cf", "#6b645a"),
    };

    internal static string Pattern(SoilKind k, string idPrefix = "p-")
    {
        var (bg, mark) = Colours(k.Main);
        var sb = new StringBuilder();
        sb.Append($"<pattern id=\"{idPrefix}{k.Key}\" width=\"16\" height=\"16\" patternUnits=\"userSpaceOnUse\"><rect width=\"16\" height=\"16\" fill=\"{bg}\"/>");
        sb.Append($"<g fill=\"none\" stroke=\"{mark}\" stroke-width=\"1.2\" stroke-linecap=\"round\">");
        switch (k.Main)
        {
            case Soil.Kies:
                sb.Append("<circle cx=\"4\" cy=\"4\" r=\"2.6\"/><circle cx=\"11.5\" cy=\"10.5\" r=\"2.6\"/>");
                break;
            case Soil.Sand:
                sb.Append($"<g fill=\"{mark}\" stroke=\"none\"><circle cx=\"3\" cy=\"3\" r=\"1\"/><circle cx=\"11\" cy=\"5\" r=\"1\"/><circle cx=\"6\" cy=\"11\" r=\"1\"/><circle cx=\"14\" cy=\"13\" r=\"1\"/></g>");
                break;
            case Soil.Schluff:
                sb.Append("<path d=\"M1.5 4.5H8M9 12.5H14.5\"/>");
                break;
            case Soil.Ton:
                sb.Append("<path d=\"M0 4H16M0 12H16\"/>");
                break;
            case Soil.Oberboden:
                sb.Append("<path d=\"M4 2.5V7.5M11 9V14M0 8H16\" stroke-width=\"1\"/>");
                break;
            default:
                sb.Append("<path d=\"M2 14L14 2\"/>");
                break;
        }

        switch (k.Second)
        {
            case 's': sb.Append($"<g fill=\"{mark}\" stroke=\"none\"><circle cx=\"13\" cy=\"3\" r=\"0.9\"/><circle cx=\"2\" cy=\"13\" r=\"0.9\"/></g>"); break;
            case 'g': sb.Append("<circle cx=\"13.2\" cy=\"2.8\" r=\"1.7\"/><circle cx=\"2.5\" cy=\"13\" r=\"1.7\"/>"); break;
            case 'u': sb.Append("<path d=\"M9.5 2.5H14.5M1 8.5H5.5\"/>"); break;
            case 't': sb.Append("<path d=\"M10 1H16M0 15H6\"/>"); break;
        }

        sb.Append("</g>");
        if (k.Fill)
        {
            // Auffüllung: diagonal cross hatch over the soil pattern.
            sb.Append("<path d=\"M-2 6L6 -2M-2 14L14 -2M2 18L18 2M10 18L18 10\" stroke=\"#3a3a3a\" stroke-opacity=\"0.55\" stroke-width=\"1\" fill=\"none\"/>");
        }

        sb.Append("</pattern>");
        return sb.ToString();
    }

    /// <summary>German legend text of a soil kind, e.g. "Auffüllung: Schluff, sandig".</summary>
    internal static string Name(SoilKind k)
    {
        string main = k.Main switch
        {
            Soil.Kies => "Kies",
            Soil.Sand => "Sand",
            Soil.Schluff => "Schluff",
            Soil.Ton => "Ton",
            Soil.Oberboden => "Mutterboden",
            _ => "Boden (nicht näher bestimmt)",
        };
        string second = k.Second switch
        {
            's' => ", sandig",
            'g' => ", kiesig",
            'u' => ", schluffig",
            't' => ", tonig",
            _ => string.Empty,
        };
        return (k.Fill ? "Auffüllung: " : string.Empty) + main + second;
    }
}
