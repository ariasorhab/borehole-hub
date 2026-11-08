using System.Text;
using BoreholeHub.Core.Entities;
using static BoreholeHub.Web.Drawing.DrawingLayout;
using static BoreholeHub.Web.Drawing.SoilPatterns;

namespace BoreholeHub.Web.Drawing;

/// <summary>
/// Geological cross-section ("Profilschnitt") through 2 to 4 boreholes as one inline SVG string.
/// Vertical axis: absolute height in m NHN. Horizontal axis: true distance along the polyline through the points (EPSG:25832).
/// Between two neighbouring boreholes the layers are drawn as a schematic interpretation: the boundaries of one
/// Homogenbereich (B1/B2/B3) are joined by straight lines; a Homogenbereich missing in the neighbour wedges out halfway.
/// Text and lines use currentColor or CSS classes (see profilschnitt.css); only soil fills and hatches are fixed colours.
/// </summary>
public static class ProfilschnittSvg
{
    /// <summary>Caption that must accompany every section.</summary>
    public const string Caption = "Schematische Interpretation zwischen den Aufschlüssen – keine Messung zwischen den Punkten.";

    private const double Width = 1000;
    private const double PlotLeft = 84;
    private const double PlotRight = 916;
    private const double LabelBand = 58;
    private const double MaxPlotH = 460;
    private const double AxisBand = 58;
    private const double ColW = 40;
    private const string PatternPrefix = "ps-";
    private static readonly int[] Exaggerations = [1, 2, 5, 10, 20, 50];

    /// <summary>Layers of one Homogenbereich in one borehole.</summary>
    private sealed record Body(string Key, double TopDepth, double BottomDepth, SoilKind Kind, double MeanVon);

    private sealed record Column(Aufschluss A, double Station, double X, List<Schicht> Layers, List<SoilKind> Kinds, List<Body> Bodies);

    public static string Render(IReadOnlyList<Aufschluss> punkte, IReadOnlyDictionary<int, IReadOnlyList<Schicht>> schichten)
    {
        ArgumentNullException.ThrowIfNull(punkte);
        ArgumentNullException.ThrowIfNull(schichten);
        if (punkte.Count < 2)
        {
            throw new ArgumentException("Ein Profilschnitt braucht mindestens zwei Aufschlüsse.", nameof(punkte));
        }

        // ---- stations along the polyline ----
        var stations = new List<double> { 0 };
        for (int i = 1; i < punkte.Count; i++)
        {
            stations.Add(stations[^1] + Dist(punkte[i - 1], punkte[i]));
        }

        double length = Math.Max(1, stations[^1]);
        double margin = Math.Max(2.5, 0.09 * length);
        double sx = (PlotRight - PlotLeft) / (length + 2 * margin);
        double X(double station) => PlotLeft + (station + margin) * sx;

        // ---- vertical range and exaggeration ----
        double maxTop = punkte.Max(a => a.AnsatzhoeheMNhn);
        double minBottom = punkte.Min(a => a.AnsatzhoeheMNhn - a.EndteufeM);
        double eMin = Math.Floor(minBottom);
        double eMax = Math.Ceiling(maxTop + 0.5);
        if (eMax - eMin < 2) { eMax = eMin + 2; }
        double range = eMax - eMin;
        int n = 1;
        foreach (int candidate in Exaggerations)
        {
            if (range * sx * candidate <= MaxPlotH) { n = candidate; }
        }

        double sy = sx * n;
        double plotH = range * sy;

        // ---- columns: widest allowed so that neighbours never touch ----
        double minGapPx = double.MaxValue;
        for (int i = 1; i < punkte.Count; i++)
        {
            minGapPx = Math.Min(minGapPx, (stations[i] - stations[i - 1]) * sx);
        }

        double colW = Math.Clamp(minGapPx * 0.45, 12, ColW);

        // label rows: stagger when names would collide
        var labelRow = new int[punkte.Count];
        var lastRowX = new List<double>();
        for (int i = 0; i < punkte.Count; i++)
        {
            double x = X(stations[i]);
            int row = 0;
            while (row < lastRowX.Count && x - lastRowX[row] < 88) { row++; }
            if (row == lastRowX.Count) { lastRowX.Add(double.NegativeInfinity); }
            lastRowX[row] = x;
            labelRow[i] = row;
        }

        int rows = Math.Max(1, labelRow.Max() + 1);
        double band = LabelBand + (rows - 1) * 30;
        double yTop = HeaderH + band;
        double Y(double elev) => yTop + (eMax - elev) * sy;

        var cols = new List<Column>();
        for (int i = 0; i < punkte.Count; i++)
        {
            var a = punkte[i];
            var layers = (schichten.TryGetValue(a.Id, out var l) ? l : []).OrderBy(s => s.VonM).ToList();
            var kinds = layers.Select(Classify).ToList();
            cols.Add(new Column(a, stations[i], X(stations[i]), layers, kinds, BuildBodies(layers, kinds)));
        }

        // ---- legend rows ----
        var usedKinds = cols.SelectMany(c => c.Kinds).GroupBy(k => k.Key).Select(g => g.First())
            .OrderBy(k => k.Main).ThenBy(k => k.Fill).ThenBy(k => k.Second).ToList();
        int legendItems = usedKinds.Count + 4;
        const int legendPerRow = 4;
        int legendRows = (legendItems + legendPerRow - 1) / legendPerRow;

        double plotBottom = yTop + plotH;
        double legendTop = plotBottom + AxisBand + 18;
        double captionTop = legendTop + legendRows * 24 + 14;
        double height = captionTop + 56;

        var sb = new StringBuilder();
        string names = string.Join(" – ", punkte.Select(p => p.Bezeichnung));
        sb.Append($"<svg class=\"profilschnitt\" viewBox=\"0 0 {N(Width)} {N(height)}\" xmlns=\"http://www.w3.org/2000/svg\" role=\"img\" aria-labelledby=\"ps-title ps-desc\" preserveAspectRatio=\"xMinYMin meet\">");
        sb.Append($"<title id=\"ps-title\">Profilschnitt {Esc(names)}</title>");
        sb.Append($"<desc id=\"ps-desc\">{Esc(Describe(cols, length, n))}</desc>");

        // ---- patterns ----
        sb.Append("<defs>");
        foreach (var k in usedKinds)
        {
            sb.Append(Pattern(k, PatternPrefix));
        }

        sb.Append("</defs>");

        Header(sb, Width, $"Profilschnitt {names}",
            $"Länge {Fmt(length, 1)} m · Höhen {Fmt(minBottom, 1)} bis {Fmt(maxTop, 1)} m NHN · Überhöhung 1:{n}");

        // ---- grid and axes ----
        for (double e = eMin; e <= eMax + 1e-9; e += 1)
        {
            double y = Y(e);
            sb.Append($"<line class=\"ps-grid\" x1=\"{N(PlotLeft)}\" y1=\"{N(y)}\" x2=\"{N(PlotRight)}\" y2=\"{N(y)}\"/>");
            sb.Append($"<text class=\"ps-tick\" x=\"{N(PlotLeft - 10)}\" y=\"{N(y + 4)}\" text-anchor=\"end\">{Fmt(e, 0)}</text>");
            sb.Append($"<text class=\"ps-tick\" x=\"{N(PlotRight + 10)}\" y=\"{N(y + 4)}\">{Fmt(e, 0)}</text>");
        }

        double step = NiceStep(length);
        var stationXs = cols.Select(c => c.X).ToList();
        for (double d = 0; d <= length + 1e-9; d += step)
        {
            double x = X(d);
            sb.Append($"<line class=\"ps-grid\" x1=\"{N(x)}\" y1=\"{N(yTop)}\" x2=\"{N(x)}\" y2=\"{N(plotBottom)}\"/>");
            sb.Append($"<line class=\"ps-axis\" x1=\"{N(x)}\" y1=\"{N(plotBottom)}\" x2=\"{N(x)}\" y2=\"{N(plotBottom + 6)}\"/>");
            if (stationXs.All(sxp => Math.Abs(sxp - x) > 30))
            {
                sb.Append($"<text class=\"ps-tick\" x=\"{N(x)}\" y=\"{N(plotBottom + 20)}\" text-anchor=\"middle\">{Fmt(d, 0)}</text>");
            }
        }

        sb.Append($"<line class=\"ps-axis\" x1=\"{N(PlotLeft)}\" y1=\"{N(yTop)}\" x2=\"{N(PlotLeft)}\" y2=\"{N(plotBottom)}\"/>");
        sb.Append($"<line class=\"ps-axis\" x1=\"{N(PlotRight)}\" y1=\"{N(yTop)}\" x2=\"{N(PlotRight)}\" y2=\"{N(plotBottom)}\"/>");
        sb.Append($"<line class=\"ps-axis\" x1=\"{N(PlotLeft)}\" y1=\"{N(plotBottom)}\" x2=\"{N(PlotRight)}\" y2=\"{N(plotBottom)}\"/>");
        sb.Append($"<text class=\"ps-axislabel\" x=\"{N(PlotLeft - 10)}\" y=\"{N(yTop - 12)}\" text-anchor=\"end\">m NHN</text>");
        sb.Append($"<text class=\"ps-axislabel\" x=\"{N(PlotRight + 10)}\" y=\"{N(yTop - 12)}\">m NHN</text>");
        sb.Append($"<text class=\"ps-axislabel\" x=\"{N((PlotLeft + PlotRight) / 2)}\" y=\"{N(plotBottom + 46)}\" text-anchor=\"middle\">Abstand entlang des Schnitts [m]</text>");

        // ---- interpreted bodies between the columns ----
        sb.Append("<g class=\"ps-bodies\">");
        for (int i = 0; i + 1 < cols.Count; i++)
        {
            AppendSpan(sb, cols[i], cols[i + 1], colW, Y);
        }

        sb.Append("</g>");

        // ---- ground surface ----
        var g0 = cols[0];
        var gN = cols[^1];
        sb.Append($"<line class=\"ps-ground-ext\" x1=\"{N(PlotLeft)}\" y1=\"{N(Y(g0.A.AnsatzhoeheMNhn))}\" x2=\"{N(g0.X - colW / 2)}\" y2=\"{N(Y(g0.A.AnsatzhoeheMNhn))}\"/>");
        sb.Append($"<line class=\"ps-ground-ext\" x1=\"{N(gN.X + colW / 2)}\" y1=\"{N(Y(gN.A.AnsatzhoeheMNhn))}\" x2=\"{N(PlotRight)}\" y2=\"{N(Y(gN.A.AnsatzhoeheMNhn))}\"/>");
        var pts = new StringBuilder();
        for (int i = 0; i < cols.Count; i++)
        {
            double yy = Y(cols[i].A.AnsatzhoeheMNhn);
            pts.Append($"{N(cols[i].X - colW / 2)},{N(yy)} {N(cols[i].X + colW / 2)},{N(yy)} ");
        }

        sb.Append($"<polyline class=\"ps-ground\" points=\"{pts.ToString().TrimEnd()}\"/>");

        // ---- columns ----
        foreach (var c in cols)
        {
            sb.Append($"<g class=\"ps-column\" data-aufschluss=\"{c.A.Id}\" data-bezeichnung=\"{Esc(c.A.Bezeichnung)}\" data-station=\"{N(c.Station)}\">");
            for (int i = 0; i < c.Layers.Count; i++)
            {
                var s = c.Layers[i];
                double ya = Y(c.A.AnsatzhoeheMNhn - s.VonM), yb = Y(c.A.AnsatzhoeheMNhn - s.BisM);
                sb.Append($"<rect class=\"ps-layer\" data-von=\"{N(s.VonM)}\" data-bis=\"{N(s.BisM)}\" x=\"{N(c.X - colW / 2)}\" y=\"{N(ya)}\" width=\"{N(colW)}\" height=\"{N(yb - ya)}\" fill=\"url(#{PatternPrefix}{c.Kinds[i].Key})\"/>");
                sb.Append($"<line class=\"ps-colline\" x1=\"{N(c.X - colW / 2)}\" y1=\"{N(yb)}\" x2=\"{N(c.X + colW / 2)}\" y2=\"{N(yb)}\"/>");
            }

            double top = Y(c.A.AnsatzhoeheMNhn), end = Y(c.A.AnsatzhoeheMNhn - c.A.EndteufeM);
            sb.Append($"<rect class=\"ps-frame\" x=\"{N(c.X - colW / 2)}\" y=\"{N(top)}\" width=\"{N(colW)}\" height=\"{N(end - top)}\"/>");
            sb.Append($"<line class=\"ps-et\" x1=\"{N(c.X - colW / 2 - 6)}\" y1=\"{N(end)}\" x2=\"{N(c.X + colW / 2 + 6)}\" y2=\"{N(end)}\"/>");
            sb.Append($"<text class=\"ps-et-text\" x=\"{N(c.X)}\" y=\"{N(end + 16)}\" text-anchor=\"middle\">ET {Fmt(c.A.EndteufeM, 2)} m</text>");
            sb.Append("</g>");
        }

        // ---- groundwater: dashed line between the boreholes that met water, symbol and value at each ----
        var wet = cols.Where(c => c.A.GrundwasserM is not null).ToList();
        if (wet.Count >= 2)
        {
            var gw = string.Join(" ", wet.Select(c => $"{N(c.X)},{N(Y(c.A.AnsatzhoeheMNhn - c.A.GrundwasserM!.Value))}"));
            sb.Append($"<polyline class=\"ps-gw-line\" points=\"{gw}\"/>");
        }

        foreach (var c in wet)
        {
            double elev = c.A.AnsatzhoeheMNhn - c.A.GrundwasserM!.Value;
            double y = Y(elev);
            sb.Append($"<path class=\"ps-gw-sym\" d=\"M{N(c.X - 7)},{N(y - 11)} L{N(c.X + 7)},{N(y - 11)} L{N(c.X)},{N(y)} Z\"/>");
            sb.Append($"<line class=\"ps-gw-sym-line\" x1=\"{N(c.X - 10)}\" y1=\"{N(y - 11)}\" x2=\"{N(c.X + 10)}\" y2=\"{N(y - 11)}\"/>");
            sb.Append($"<text class=\"ps-gw-text\" x=\"{N(c.X + colW / 2 + 8)}\" y=\"{N(y - 5)}\">{Fmt(elev, 2)}</text>");
        }

        // ---- labels above the columns ----
        foreach (var (c, i) in cols.Select((c, i) => (c, i)))
        {
            double yName = HeaderH + 16 + labelRow[i] * 30;
            double yTopGround = Y(c.A.AnsatzhoeheMNhn);
            sb.Append($"<text class=\"ps-col-name\" x=\"{N(c.X)}\" y=\"{N(yName)}\" text-anchor=\"middle\">{Esc(c.A.Bezeichnung)}</text>");
            sb.Append($"<text class=\"ps-col-sub\" x=\"{N(c.X)}\" y=\"{N(yName + 14)}\" text-anchor=\"middle\">{Fmt(c.A.AnsatzhoeheMNhn, 2)} m NHN</text>");
            sb.Append($"<line class=\"ps-leader\" x1=\"{N(c.X)}\" y1=\"{N(yName + 19)}\" x2=\"{N(c.X)}\" y2=\"{N(yTopGround - 2)}\"/>");
            sb.Append($"<text class=\"ps-station\" x=\"{N(c.X)}\" y=\"{N(plotBottom + 20)}\" text-anchor=\"middle\">{Fmt(c.Station, 1)}</text>");
            sb.Append($"<path class=\"ps-station-mark\" d=\"M{N(c.X - 5)},{N(plotBottom + 7)} L{N(c.X + 5)},{N(plotBottom + 7)} L{N(c.X)},{N(plotBottom)} Z\"/>");
        }

        // ---- legend ----
        double lx0 = 16, colWLegend = 242;
        var items = new List<Action<StringBuilder, double, double>>();
        foreach (var k in usedKinds)
        {
            var kind = k;
            items.Add((b, x, y) =>
            {
                b.Append($"<rect class=\"ps-swatch\" x=\"{N(x)}\" y=\"{N(y)}\" width=\"30\" height=\"16\" rx=\"2\" fill=\"url(#{PatternPrefix}{kind.Key})\"/>");
                b.Append($"<text class=\"ps-legend-text\" x=\"{N(x + 38)}\" y=\"{N(y + 12)}\">{Esc(Name(kind))}</text>");
            });
        }

        items.Add((b, x, y) =>
        {
            b.Append($"<line class=\"ps-gw-line\" x1=\"{N(x)}\" y1=\"{N(y + 8)}\" x2=\"{N(x + 30)}\" y2=\"{N(y + 8)}\"/>");
            b.Append($"<path class=\"ps-gw-sym\" d=\"M{N(x + 8)},{N(y + 1)} L{N(x + 22)},{N(y + 1)} L{N(x + 15)},{N(y + 10)} Z\"/>");
            b.Append($"<text class=\"ps-legend-text\" x=\"{N(x + 38)}\" y=\"{N(y + 12)}\">Grundwasser (m NHN)</text>");
        });
        items.Add((b, x, y) =>
        {
            b.Append($"<line class=\"ps-et\" x1=\"{N(x)}\" y1=\"{N(y + 8)}\" x2=\"{N(x + 30)}\" y2=\"{N(y + 8)}\"/>");
            b.Append($"<text class=\"ps-legend-text\" x=\"{N(x + 38)}\" y=\"{N(y + 12)}\">Endteufe (ET)</text>");
        });
        items.Add((b, x, y) =>
        {
            b.Append($"<line class=\"ps-interp\" x1=\"{N(x)}\" y1=\"{N(y + 8)}\" x2=\"{N(x + 30)}\" y2=\"{N(y + 8)}\"/>");
            b.Append($"<text class=\"ps-legend-text\" x=\"{N(x + 38)}\" y=\"{N(y + 12)}\">Interpretierte Grenze</text>");
        });
        items.Add((b, x, y) =>
        {
            b.Append($"<rect class=\"ps-hb\" x=\"{N(x + 3)}\" y=\"{N(y + 1)}\" width=\"24\" height=\"14\" rx=\"7\"/>");
            b.Append($"<text class=\"ps-hb-text\" x=\"{N(x + 15)}\" y=\"{N(y + 12)}\" text-anchor=\"middle\">B2</text>");
            b.Append($"<text class=\"ps-legend-text\" x=\"{N(x + 38)}\" y=\"{N(y + 12)}\">Homogenbereich</text>");
        });

        for (int i = 0; i < items.Count; i++)
        {
            items[i](sb, lx0 + (i % legendPerRow) * colWLegend, legendTop + (i / legendPerRow) * 24);
        }

        // ---- caption ----
        sb.Append($"<text class=\"ps-caption\" x=\"16\" y=\"{N(captionTop + 14)}\"><tspan class=\"ps-caption-strong\">{Esc(Caption)}</tspan></text>");
        sb.Append($"<text class=\"ps-caption\" x=\"16\" y=\"{N(captionTop + 34)}\">Überhöhung 1:{n} (Höhen {n}-fach gegenüber den Längen) · Abstände entlang des Linienzugs in EPSG:25832 · Höhen in m NHN</text>");

        sb.Append("</svg>");
        return sb.ToString();
    }

    // ---------- geometry of the interpreted span between two columns ----------

    private static void AppendSpan(StringBuilder sb, Column a, Column b, double colW, Func<double, double> y)
    {
        double xr = a.X + colW / 2, xl = b.X - colW / 2;
        if (xl <= xr) { return; }
        double xm = (xr + xl) / 2;

        // stack order: by mean start depth over the boreholes that have the Homogenbereich
        var keys = a.Bodies.Concat(b.Bodies).GroupBy(x => x.Key)
            .Select(g => (Key: g.Key, Order: g.Average(x => x.MeanVon)))
            .OrderBy(t => t.Order).ThenBy(t => t.Key, StringComparer.Ordinal).Select(t => t.Key).ToList();

        // thickness (m) of each body at the three breakpoints xr, xm, xl
        double Thick(Body? body) => body is null ? 0 : Math.Max(0, body.BottomDepth - body.TopDepth);
        double groundR = a.A.AnsatzhoeheMNhn, groundL = b.A.AnsatzhoeheMNhn, groundM = (groundR + groundL) / 2;

        double cumR = 0, cumM = 0, cumL = 0;
        foreach (var key in keys)
        {
            var ba = a.Bodies.FirstOrDefault(x => x.Key == key);
            var bb = b.Bodies.FirstOrDefault(x => x.Key == key);
            double tR = Thick(ba), tL = Thick(bb);
            double tM = ba is not null && bb is not null ? (tR + tL) / 2 : 0;

            double topR = groundR - cumR, topM = groundM - cumM, topL = groundL - cumL;
            cumR += tR; cumM += tM; cumL += tL;
            double botR = groundR - cumR, botM = groundM - cumM, botL = groundL - cumL;

            var left = ba?.Kind ?? bb!.Kind;
            var right = bb?.Kind ?? ba!.Kind;
            string cls = ba is not null && bb is not null ? "ps-body" : "ps-body ps-wedge";
            string data = $"data-hb=\"{Esc(key)}\"";

            if (left.Key == right.Key)
            {
                string poly = $"{N(xr)},{N(y(topR))} {N(xm)},{N(y(topM))} {N(xl)},{N(y(topL))} {N(xl)},{N(y(botL))} {N(xm)},{N(y(botM))} {N(xr)},{N(y(botR))}";
                sb.Append($"<polygon class=\"{cls}\" {data} points=\"{poly}\" fill=\"url(#{PatternPrefix}{left.Key})\"/>");
            }
            else
            {
                sb.Append($"<polygon class=\"{cls}\" {data} points=\"{N(xr)},{N(y(topR))} {N(xm)},{N(y(topM))} {N(xm)},{N(y(botM))} {N(xr)},{N(y(botR))}\" fill=\"url(#{PatternPrefix}{left.Key})\"/>");
                sb.Append($"<polygon class=\"{cls}\" {data} points=\"{N(xm)},{N(y(topM))} {N(xl)},{N(y(topL))} {N(xl)},{N(y(botL))} {N(xm)},{N(y(botM))}\" fill=\"url(#{PatternPrefix}{right.Key})\"/>");
            }

            // interpreted boundaries (dashed): the bottom of each body, and the top of the first one is the ground line
            sb.Append($"<polyline class=\"ps-interp\" points=\"{N(xr)},{N(y(botR))} {N(xm)},{N(y(botM))} {N(xl)},{N(y(botL))}\"/>");

            // Homogenbereich tag in the middle if the body is thick enough
            if (ba is not null && bb is not null && y(botM) - y(topM) >= 18 && xl - xr >= 64)
            {
                double hpx = y(botM) - y(topM);
                double cy = y(topM) + Math.Min(hpx / 2, Math.Max(10, 0.3 * hpx));
                sb.Append($"<g class=\"ps-hbtag\"><rect class=\"ps-hb\" x=\"{N(xm - 14)}\" y=\"{N(cy - 8)}\" width=\"28\" height=\"16\" rx=\"8\"/><text class=\"ps-hb-text\" x=\"{N(xm)}\" y=\"{N(cy + 4)}\" text-anchor=\"middle\">{Esc(key)}</text></g>");
            }
        }
    }

    private static List<Body> BuildBodies(List<Schicht> layers, List<SoilKind> kinds)
    {
        var result = new List<Body>();
        var groups = layers.Select((s, i) => (s, kind: kinds[i]))
            .GroupBy(t => string.IsNullOrWhiteSpace(t.s.Homogenbereich) ? "–" : t.s.Homogenbereich!.Trim());
        foreach (var g in groups)
        {
            var dominant = g.OrderByDescending(t => t.s.BisM - t.s.VonM).First().kind;
            result.Add(new Body(g.Key, g.Min(t => t.s.VonM), g.Max(t => t.s.BisM), dominant, g.Min(t => t.s.VonM)));
        }

        return result;
    }

    // ---------- helpers ----------

    internal static double Dist(Aufschluss a, Aufschluss b) =>
        Math.Sqrt(Math.Pow(a.Rechtswert - b.Rechtswert, 2) + Math.Pow(a.Hochwert - b.Hochwert, 2));

    private static double NiceStep(double length)
    {
        foreach (double s in new[] { 1.0, 2, 5, 10, 20, 50, 100, 200, 500, 1000 })
        {
            if (length / s <= 8) { return s; }
        }

        return 1000;
    }

    private static string Describe(List<Column> cols, double length, int n)
    {
        var parts = cols.Select(c =>
        {
            string gw = c.A.GrundwasserM is double g ? $", Grundwasser bei {Fmt(g, 2)} m u. GOK ({Fmt(c.A.AnsatzhoeheMNhn - g, 2)} m NHN)" : ", Grundwasser nicht angetroffen";
            return $"{c.A.Bezeichnung} bei {Fmt(c.Station, 1)} m, Ansatzhöhe {Fmt(c.A.AnsatzhoeheMNhn, 2)} m NHN, Endteufe {Fmt(c.A.EndteufeM, 2)} m{gw}";
        });
        return $"Geologischer Schnitt über {Fmt(length, 1)} m mit {cols.Count} Aufschlüssen, Überhöhung 1:{n}. " + string.Join("; ", parts) + ". " + Caption;
    }
}
