using System.Globalization;
using BoreholeHub.Core.Entities;
using BoreholeHub.Core.Services;
using BoreholeHub.Web.Drawing;
using KfModel = BoreholeHub.Web.Pages.Aufschluesse.DetailsModel;
using BoreholeHub.Web.Pages.Projekte;
using QuestPDF.Drawing;
using QuestPDF.Fluent;
using QuestPDF.Helpers;
using QuestPDF.Infrastructure;

namespace BoreholeHub.Web.Reports;

/// <summary>
/// PDF appendix ("Anlagen") of a project in the style of a German foundation-soil report:
/// Deckblatt, Anlage 1 Lageplan, Anlage 2.n Bohrprofile mit Rammdiagrammen, Anlage 3 Profilschnitt, Anlage 4 Laborergebnisse, Anlage 5 Versickerung.
/// Built with QuestPDF (Community licence, see <see cref="Lizenz"/>); the drawings are the server-side SVGs of the web pages.
/// </summary>
public static class ProjektBerichtPdf
{
    public const string Hinweis = "Demo mit synthetischen Daten – keine Gutachtenaussage";
    public const string EbvHinweis = "Beispielwerte zur Demonstration – keine amtlichen Werte der Ersatzbaustoffverordnung (EBV).";
    public const string VersickerungHinweis = "Vereinfachte Vorprüfung: Der kf-Wert ersetzt keine Bemessung nach DWA-A 138-1.";

    private const string Ink = "#22201c", Muted = "#6b645a", Line = "#d9d2c3", Sand = "#f3efe6", Rust = "#94461f", Water = "#2d6a86";
    private static readonly CultureInfo De = CultureInfo.GetCultureInfo("de-DE");
    private static readonly object InitLock = new();
    private static bool _initialised;

    /// <summary>
    /// QuestPDF Community licence: free for this non-commercial demo (QuestPDF licence terms: revenue below 1 million USD, or non-commercial use).
    /// Review before any commercial use.
    /// </summary>
    private static void Lizenz() => QuestPDF.Settings.License = LicenseType.Community;

    private static void Initialise()
    {
        lock (InitLock)
        {
            if (_initialised) { return; }
            Lizenz();
            string dir = Path.Combine(AppContext.BaseDirectory, "Fonts");
            foreach (var file in new[] { "Inter-Regular.ttf", "Inter-SemiBold.ttf" })
            {
                string path = Path.Combine(dir, file);
                if (File.Exists(path))
                {
                    using var stream = File.OpenRead(path);
                    FontManager.RegisterFont(stream);
                }
            }

            _initialised = true;
        }
    }

    /// <summary>Creates the report with the example limit values from the demo data set.</summary>
    public static byte[] Erzeugen(Project projekt) => Erzeugen(projekt, null);

    /// <summary>Creates the report; <paramref name="grenzwerte"/> are the (example) class limits from the database, null = built-in example limits.</summary>
    public static byte[] Erzeugen(Project projekt, IReadOnlyList<Grenzwert>? grenzwerte) =>
        Dokument(projekt, grenzwerte).GeneratePdf();

    /// <summary>The document itself, e.g. for page images in tests and reviews.</summary>
    public static IDocument Dokument(Project projekt, IReadOnlyList<Grenzwert>? grenzwerte = null)
    {
        ArgumentNullException.ThrowIfNull(projekt);
        Initialise();
        return new Bericht(projekt, grenzwerte ?? BeispielGrenzwerte());
    }

    /// <summary>File name "{projektname-slug}-anlagen.pdf".</summary>
    public static string Dateiname(string projektName)
    {
        var sb = new System.Text.StringBuilder();
        foreach (char c in projektName.ToLowerInvariant())
        {
            switch (c)
            {
                case 'ä': sb.Append("ae"); break;
                case 'ö': sb.Append("oe"); break;
                case 'ü': sb.Append("ue"); break;
                case 'ß': sb.Append("ss"); break;
                case >= 'a' and <= 'z':
                case >= '0' and <= '9':
                    sb.Append(c); break;
                default:
                    if (sb.Length > 0 && sb[^1] != '-') { sb.Append('-'); }
                    break;
            }
        }

        string slug = sb.ToString().Trim('-');
        return (slug.Length == 0 ? "projekt" : slug) + "-anlagen.pdf";
    }

    // Same invented example limits as the demo seed (mg/kg, classes BM-0 ... BM-F3). Used only when no limits are passed in.
    private static IReadOnlyList<Grenzwert> BeispielGrenzwerte()
    {
        string[] klassen = ["BM-0", "BM-0*", "BM-F0*", "BM-F1", "BM-F2", "BM-F3"];
        (string, double[])[] rows =
        [
            ("Arsen", [8, 12, 20, 30, 45, 60]), ("Blei", [30, 60, 100, 160, 250, 400]), ("Cadmium", [0.4, 0.8, 1.5, 2.5, 4, 6]),
            ("Chrom, gesamt", [30, 50, 80, 120, 180, 250]), ("Kupfer", [20, 40, 70, 110, 170, 250]), ("Nickel", [15, 30, 50, 80, 120, 180]),
            ("Quecksilber", [0.2, 0.4, 0.8, 1.2, 2, 3]), ("Zink", [60, 100, 160, 250, 400, 600]), ("PAK16", [3, 6, 10, 15, 25, 40]),
            ("Kohlenwasserstoffe", [100, 200, 400, 600, 800, 1000]),
        ];
        var list = new List<Grenzwert>();
        foreach (var (p, w) in rows)
        {
            for (int i = 0; i < klassen.Length; i++)
            {
                list.Add(new Grenzwert { Parameter = p, Einheit = "mg/kg", Klasse = klassen[i], Wert = w[i], Reihenfolge = i + 1, IstBeispiel = true });
            }
        }

        return list;
    }

    // ------------------------------------------------------------------------------------------------

    private sealed class Bericht : IDocument
    {
        private readonly Project _p;
        private readonly IReadOnlyList<Grenzwert> _grenz;
        private readonly List<Aufschluss> _krb;
        private readonly List<Aufschluss> _dph;

        public Bericht(Project p, IReadOnlyList<Grenzwert> grenz)
        {
            _p = p;
            _grenz = grenz;
            _krb = p.Aufschluesse.Where(a => a.Art == AufschlussArt.Kleinrammbohrung).OrderBy(a => a.Id).ToList();
            _dph = p.Aufschluesse.Where(a => a.Art == AufschlussArt.Rammsondierung).OrderBy(a => a.Id).ToList();
        }

        public DocumentMetadata GetMetadata() => new()
        {
            Title = $"Anlagen – {_p.Name}",
            Author = "BoreholeHub (Demo)",
            Subject = Hinweis,
            Language = "de-DE",
        };

        public DocumentSettings GetSettings() => DocumentSettings.Default;

        public void Compose(IDocumentContainer container)
        {
            container.Page(page => { Setup(page, false); page.Content().Element(Deckblatt); });
            container.Page(page => { Setup(page, false); page.Content().Element(Lageplan); });
            for (int i = 0; i < _krb.Count; i++)
            {
                int index = i;
                container.Page(page => { Setup(page, false); page.Content().Element(c => Bohrprofil(c, index)); });
            }

            container.Page(page => { Setup(page, true); page.Content().Element(Profilschnitt); });
            container.Page(page => { Setup(page, false); page.Content().Element(Labor); });
            container.Page(page => { Setup(page, false); page.Content().Element(Versickerung); });
        }

        // ---------- page frame ----------

        private void Setup(PageDescriptor page, bool landscape)
        {
            page.Size(landscape ? PageSizes.A4.Landscape() : PageSizes.A4);
            page.MarginHorizontal(1.6f, Unit.Centimetre);
            page.MarginTop(1.3f, Unit.Centimetre);
            page.MarginBottom(1.2f, Unit.Centimetre);
            page.DefaultTextStyle(t => t.FontFamily("Inter").FontSize(9.5f).FontColor(Ink));

            page.Header().PaddingBottom(8).Column(col =>
            {
                col.Item().Row(row =>
                {
                    row.RelativeItem().Text($"BoreholeHub · {_p.Name}").FontSize(8.5f).FontColor(Muted);
                });
                col.Item().PaddingTop(4).LineHorizontal(0.75f).LineColor(Line);
            });

            page.Footer().Column(col =>
            {
                col.Item().LineHorizontal(0.75f).LineColor(Line);
                col.Item().PaddingTop(4).Row(row =>
                {
                    row.RelativeItem().Text(Hinweis).FontSize(8.5f).FontColor(Muted);
                    row.AutoItem().Text(t =>
                    {
                        t.DefaultTextStyle(s => s.FontSize(8.5f).FontColor(Muted));
                        t.Span("Seite ");
                        t.CurrentPageNumber();
                        t.Span(" von ");
                        t.TotalPages();
                    });
                });
            });
        }

        // ---------- helpers ----------

        private static void Titel(IContainer c, string nummer, string titel, string? untertitel = null) =>
            c.Column(col =>
            {
                col.Item().Text(t => { t.Span(nummer).FontColor(Rust).SemiBold().FontSize(11); });
                col.Item().PaddingTop(1).Text(titel).SemiBold().FontSize(17);
                if (untertitel is not null)
                {
                    col.Item().PaddingTop(3).Text(untertitel).FontColor(Muted).FontSize(9.5f);
                }

                col.Item().PaddingTop(8).PaddingBottom(10).LineHorizontal(1.2f).LineColor(Ink);
            });

        private static IContainer HeadCell(IContainer c) => c.Background(Sand).BorderBottom(0.75f).BorderColor(Muted).PaddingVertical(4).PaddingHorizontal(5);

        private static IContainer BodyCell(IContainer c) => c.BorderBottom(0.5f).BorderColor(Line).PaddingVertical(3.5f).PaddingHorizontal(5);

        private static string F(double v, int d) => v.ToString("F" + d, De);

        private static void Hinweisbox(IContainer c, string text) =>
            c.PaddingTop(8).Text(text).FontSize(8.5f).FontColor(Muted).Italic();

        // ---------- Deckblatt ----------

        private void Deckblatt(IContainer c)
        {
            var datumVon = _p.Aufschluesse.Count == 0 ? (DateOnly?)null : _p.Aufschluesse.Min(a => a.Datum);
            var datumBis = _p.Aufschluesse.Count == 0 ? (DateOnly?)null : _p.Aufschluesse.Max(a => a.Datum);
            string zeitraum = datumVon is null ? "–" : datumVon == datumBis ? datumVon.Value.ToString("dd.MM.yyyy", De) : $"{datumVon:dd.MM.yyyy} – {datumBis:dd.MM.yyyy}";

            c.Column(col =>
            {
                col.Item().PaddingTop(40).Text("ANLAGEN").FontSize(11).SemiBold().FontColor(Rust).LetterSpacing(0.12f);
                col.Item().PaddingTop(6).Text("Baugrunderkundung").FontSize(30).SemiBold();
                col.Item().PaddingTop(2).Text("Anlagen zum Bericht").FontSize(16).FontColor(Muted);
                col.Item().PaddingTop(18).LineHorizontal(2).LineColor(Rust);

                col.Item().PaddingTop(22).Text(_p.Name).FontSize(18).SemiBold();
                col.Item().PaddingTop(4).Text(_p.Ort).FontSize(12).FontColor(Muted);
                if (!string.IsNullOrWhiteSpace(_p.Beschreibung))
                {
                    col.Item().PaddingTop(12).Text(_p.Beschreibung).FontSize(10.5f);
                }

                col.Item().PaddingTop(24).Table(t =>
                {
                    t.ColumnsDefinition(cd => { cd.ConstantColumn(150); cd.RelativeColumn(); });
                    void Row(string k, string v)
                    {
                        t.Cell().Element(BodyCell).Text(k).FontColor(Muted);
                        t.Cell().Element(BodyCell).Text(v);
                    }

                    Row("Kleinrammbohrungen (KRB)", _krb.Count.ToString(De));
                    Row("Rammsondierungen (DPH)", _dph.Count.ToString(De));
                    Row("Erkundungszeitraum", zeitraum);
                    Row("Koordinatensystem", "ETRS89 / UTM 32N (EPSG:25832)");
                    Row("Höhenbezug", "m NHN");
                    Row("Stand", DateTime.Now.ToString("dd.MM.yyyy", De));
                });

                col.Item().PaddingTop(28).Text("Anlagenverzeichnis").FontSize(13).SemiBold();
                col.Item().PaddingTop(6).Table(t =>
                {
                    t.ColumnsDefinition(cd => { cd.ConstantColumn(60); cd.RelativeColumn(); cd.ConstantColumn(40); });
                    void Row(string nr, string text, string loc)
                    {
                        t.Cell().Element(BodyCell).Text(nr).SemiBold();
                        t.Cell().Element(BodyCell).Text(text);
                        t.Cell().Element(BodyCell).AlignRight().Text(x => x.PageNumberOfLocation(loc));
                    }

                    Row("Anlage 1", "Lageplan der Aufschlüsse", "anlage-1");
                    for (int i = 0; i < _krb.Count; i++)
                    {
                        var d = Sonde(_krb[i]);
                        Row($"Anlage 2.{i + 1}", $"Bohrprofil {_krb[i].Bezeichnung}" + (d is null ? string.Empty : $" mit Rammdiagramm {d.Bezeichnung}"), $"anlage-2-{i + 1}");
                    }

                    Row("Anlage 3", "Profilschnitt (schematische Interpretation)", "anlage-3");
                    Row("Anlage 4", "Laborergebnisse (Beispielklassen)", "anlage-4");
                    Row("Anlage 5", "Versickerung (Vorprüfung)", "anlage-5");
                });

                col.Item().PaddingTop(26).Background(Sand).Border(0.75f).BorderColor(Line).Padding(10).Text(t =>
                {
                    t.Span(Hinweis + ". ").SemiBold();
                    t.Span("Alle Daten, Werte und Zeichnungen dieses Dokuments sind erfunden und dienen nur der Demonstration der Software.");
                });
            });
        }

        // ---------- Anlage 1 ----------

        private void Lageplan(IContainer c)
        {
            var schnitt = _krb.Count >= 2 ? ProfilschnittModel.StandardAuswahl(_krb) : [];
            string svg = SvgPrint.Apply(LageplanSvg.Render(_p.Aufschluesse, schnitt));
            c.Location("anlage-1").Column(col =>
            {
                col.Item().Element(x => Titel(x, "Anlage 1", "Lageplan", "Schematische Lage der Aufschlüsse (ohne Kartengrundlage), Koordinatengitter in EPSG:25832"));
                col.Item().Element(x => x.Svg(svg));
                col.Item().Element(x => Hinweisbox(x, "Schematischer Lageplan aus den Koordinaten der Aufschlüsse; maßstäbliche Verhältnisse nach dem Maßstabsbalken."));
            });
        }

        // ---------- Anlage 2.n ----------

        private Aufschluss? Sonde(Aufschluss krb) =>
            _dph.OrderBy(d => ProfilschnittSvg.Dist(krb, d)).FirstOrDefault();

        private void Bohrprofil(IContainer c, int index)
        {
            var a = _krb[index];
            var d = Sonde(a);
            double scale = DrawingLayout.ScaleFor(a.EndteufeM, d?.EndteufeM ?? 0);
            var layers = a.Schichten.OrderBy(s => s.VonM).ToList();
            string log = SvgPrint.Apply(BohrprofilSvg.Render(a, layers, scale));
            string? ram = d is null ? null : SvgPrint.Apply(RammdiagrammSvg.Render(d, d.Messwerte.OrderBy(m => m.TiefeBisM).ToList(), scale));
            string titel = $"Bohrprofil {a.Bezeichnung}" + (d is null ? string.Empty : $" mit Rammdiagramm {d.Bezeichnung}");
            string gw = a.GrundwasserM is double g ? $"Grundwasser {F(g, 2)} m u. GOK ({F(a.AnsatzhoeheMNhn - g, 2)} m NHN)" : "Grundwasser nicht angetroffen";

            c.Location($"anlage-2-{index + 1}").Column(col =>
            {
                col.Item().Element(x => Titel(x, $"Anlage 2.{index + 1}", titel,
                    $"Ansatzhöhe {F(a.AnsatzhoeheMNhn, 2)} m NHN · Endteufe {F(a.EndteufeM, 2)} m · {gw} · Darstellung in Anlehnung an DIN 4023 (vereinfacht)"));
                col.Item().Row(row =>
                {
                    row.Spacing(10);
                    row.RelativeItem(760).Element(x => x.Svg(log));
                    if (ram is not null)
                    {
                        row.RelativeItem(500).Element(x => x.Svg(ram));
                    }
                });
                col.Item().PaddingTop(14).Text($"Schichtenverzeichnis {a.Bezeichnung}").SemiBold().FontSize(11);
                col.Item().PaddingTop(4).DefaultTextStyle(t => t.FontSize(8)).Table(t =>
                {
                    t.ColumnsDefinition(cd =>
                    {
                        cd.ConstantColumn(50); cd.ConstantColumn(46); cd.RelativeColumn(3); cd.RelativeColumn(2); cd.ConstantColumn(40);
                        cd.ConstantColumn(50); cd.ConstantColumn(38); cd.ConstantColumn(50);
                    });
                    t.Header(h =>
                    {
                        foreach (var head in new[] { "Von–Bis\n[m]", "Kurz-\nzeichen", "Beschreibung", "Farbe, Zustand", "Boden-\ngruppe", "Homogen-\nbereich", "Frost-\nklasse", "kf [m/s]" })
                        {
                            h.Cell().Element(HeadCell).Text(head).SemiBold();
                        }
                    });
                    foreach (var l in layers)
                    {
                        t.Cell().Element(BodyCell).Text($"{F(l.VonM, 2)}–{F(l.BisM, 2)}");
                        t.Cell().Element(BodyCell).Text(l.Kurzzeichen);
                        t.Cell().Element(BodyCell).Text(l.Beschreibung);
                        t.Cell().Element(BodyCell).Text(string.Join("\n", new[] { l.Farbe, l.Zustand }.Where(v => !string.IsNullOrWhiteSpace(v))));
                        t.Cell().Element(BodyCell).Text(l.Bodengruppe ?? "–");
                        t.Cell().Element(BodyCell).Text(l.Homogenbereich ?? "–");
                        t.Cell().Element(BodyCell).Text(l.Frostklasse ?? "–");
                        t.Cell().Element(BodyCell).Text(KfModel.KfText(l.KfWertMs));
                    }
                });
                if (d is not null)
                {
                    col.Item().Element(x => Hinweisbox(x, $"{d.Bezeichnung} liegt {F(ProfilschnittSvg.Dist(a, d), 1)} m von {a.Bezeichnung} entfernt (nächstgelegene Rammsondierung). N10 = Schläge je 10 cm Eindringung."));
                }
            });
        }

        // ---------- Anlage 3 ----------

        private void Profilschnitt(IContainer c)
        {
            c.Decoration(dec =>
            {
                if (_krb.Count < 2)
                {
                    dec.Before().Location("anlage-3").Element(x => Titel(x, "Anlage 3", "Profilschnitt"));
                    dec.Content().Text("Für einen Profilschnitt werden mindestens zwei Kleinrammbohrungen benötigt.");
                    return;
                }

                var sel = ProfilschnittModel.StandardAuswahl(_krb);
                var dict = sel.ToDictionary(a => a.Id, a => (IReadOnlyList<Schicht>)a.Schichten.OrderBy(s => s.VonM).ToList());
                string svg = SvgPrint.Apply(ProfilschnittSvg.Render(sel, dict));
                dec.Before().Location("anlage-3").Element(x => Titel(x, "Anlage 3", "Profilschnitt " + string.Join(" – ", sel.Select(a => a.Bezeichnung)),
                    "Geologischer Schnitt durch die Kleinrammbohrungen, Höhen in m NHN"));
                // the drawing is sized by width so that it fits the space left on the landscape page (about 385 pt below the title)
                var vb = System.Text.RegularExpressions.Regex.Match(svg, "viewBox=\"0 0 ([0-9.]+) ([0-9.]+)\"");
                double ratio = double.Parse(vb.Groups[2].Value, CultureInfo.InvariantCulture) / double.Parse(vb.Groups[1].Value, CultureInfo.InvariantCulture);
                float width = (float)Math.Min(760, 385 / ratio);
                dec.Content().AlignCenter().Width(width).Svg(svg);
            });
        }

        // ---------- Anlage 4 ----------

        private void Labor(IContainer c)
        {
            var proben = _krb.SelectMany(a => a.Proben.OrderBy(p => p.VonM).Select(p => (Aufschluss: a, Probe: p))).ToList();
            c.Location("anlage-4").Column(col =>
            {
                col.Item().Element(x => Titel(x, "Anlage 4", "Laborergebnisse",
                    "Analysenwerte je Probe mit Einstufung in Beispielklassen (angelehnt an die Materialklassen der Ersatzbaustoffverordnung)"));

                if (proben.Count == 0)
                {
                    col.Item().Text("Es liegen keine Laborergebnisse vor.");
                }

                for (int i = 0; i < proben.Count; i += 2)
                {
                    var pair = proben.Skip(i).Take(2).ToList();
                    col.Item().ShowEntire().PaddingBottom(14).Row(row =>
                    {
                        row.Spacing(16);
                        foreach (var (a, p) in pair)
                        {
                            row.RelativeItem().Element(x => Probenblock(x, a, p));
                        }

                        if (pair.Count == 1) { row.RelativeItem(); }
                    });
                }

                // limit table
                col.Item().PaddingTop(6).Text("Beispiel-Grenzwerte je Klasse [mg/kg]").SemiBold().FontSize(12);
                var klassen = _grenz.GroupBy(g => g.Klasse).OrderBy(g => g.Min(x => x.Reihenfolge)).Select(g => g.Key).ToList();
                col.Item().PaddingTop(5).Table(t =>
                {
                    t.ColumnsDefinition(cd => { cd.RelativeColumn(2.4f); foreach (var _ in klassen) { cd.RelativeColumn(1); } });
                    t.Header(h =>
                    {
                        h.Cell().Element(HeadCell).Text("Parameter").SemiBold();
                        foreach (var k in klassen) { h.Cell().Element(HeadCell).AlignRight().Text(k).SemiBold(); }
                    });
                    foreach (var g in _grenz.GroupBy(x => x.Parameter))
                    {
                        t.Cell().Element(BodyCell).Text(g.Key);
                        foreach (var k in klassen)
                        {
                            var w = g.FirstOrDefault(x => x.Klasse == k);
                            t.Cell().Element(BodyCell).AlignRight().Text(w is null ? "–" : w.Wert.ToString("0.##", De));
                        }
                    }
                });
                col.Item().PaddingTop(5).Text(EbvHinweis).FontSize(8.5f).FontColor(Muted).Italic();
                col.Item().PaddingTop(3).Text("Einstufung: strengste Klasse, deren Beispiel-Grenzwert eingehalten wird; „> Klasse“ = der Wert überschreitet alle Beispiel-Grenzwerte.").FontSize(8.5f).FontColor(Muted);
            });
        }

        private void Probenblock(IContainer c, Aufschluss a, Probe p)
        {
            var results = LabCheckService.Evaluate(p.Laborwerte.OrderBy(l => l.Id), _grenz);
            double mid = (p.VonM + p.BisM) / 2;
            var s = a.Schichten.FirstOrDefault(x => x.VonM <= mid && mid < x.BisM);
            string schicht = s is null ? "–" : (s.Beschreibung.Contains(':') ? s.Beschreibung[..s.Beschreibung.IndexOf(':')].Trim() : s.Kurzzeichen);
            c.DefaultTextStyle(t => t.FontSize(8.5f)).Column(pc =>
            {
                pc.Item().Text(t =>
                {
                    t.Span(p.Bezeichnung).SemiBold().FontSize(11);
                    t.Span($"   {F(p.VonM, 2)}–{F(p.BisM, 2)} m u. GOK · {schicht}").FontColor(Muted);
                });
                pc.Item().PaddingTop(4).Table(t =>
                {
                    t.ColumnsDefinition(cd => { cd.RelativeColumn(3.2f); cd.RelativeColumn(1.3f); cd.RelativeColumn(1.4f); cd.RelativeColumn(1.7f); });
                    t.Header(h =>
                    {
                        h.Cell().Element(HeadCell).Text("Parameter").SemiBold();
                        h.Cell().Element(HeadCell).AlignRight().Text("Wert").SemiBold();
                        h.Cell().Element(HeadCell).Text("Einheit").SemiBold();
                        h.Cell().Element(HeadCell).Text("Einstufung").SemiBold();
                    });
                    foreach (var r in results)
                    {
                        string klasse = r.Ueberschritten ? "> " + r.UeberschritteneGrenze : r.EingehalteneKlasse ?? "–";
                        string farbe = r.Ueberschritten ? "#b23a2e" : r.EingehalteneKlasse is "BM-0" ? "#3d7a4f" : Ink;
                        t.Cell().Element(BodyCell).Text(r.Parameter);
                        t.Cell().Element(BodyCell).AlignRight().Text(r.Wert.ToString("0.##", De));
                        t.Cell().Element(BodyCell).Text(r.Einheit);
                        t.Cell().Element(BodyCell).Text(klasse).SemiBold().FontColor(farbe);
                    }
                });
                pc.Item().PaddingTop(3).Text(EbvHinweis).FontSize(7.5f).FontColor(Muted).Italic();
            });
        }

        // ---------- Anlage 5 ----------

        private void Versickerung(IContainer c)
        {
            var rows = _krb.SelectMany(a => a.Schichten.Where(s => s.KfWertMs is not null).OrderBy(s => s.VonM).Select(s => (Aufschluss: a, Schicht: s))).ToList();
            static string Limit(double v) => KfModel.KfText(v).Replace("1,0 · ", "1 · ");

            c.Location("anlage-5").Column(col =>
            {
                col.Item().Element(x => Titel(x, "Anlage 5", "Versickerung (Vorprüfung)", "Durchlässigkeitsbeiwerte kf der Schichten und einfache Beurteilung der Versickerungseignung"));
                col.Item().Table(t =>
                {
                    t.ColumnsDefinition(cd => { cd.ConstantColumn(60); cd.RelativeColumn(2.0f); cd.ConstantColumn(64); cd.RelativeColumn(1.5f); cd.RelativeColumn(2.9f); });
                    t.Header(h =>
                    {
                        h.Cell().Element(HeadCell).Text("Aufschluss").SemiBold();
                        h.Cell().Element(HeadCell).Text("Schicht [m u. GOK]").SemiBold();
                        h.Cell().Element(HeadCell).AlignRight().Text("kf [m/s]").SemiBold();
                        h.Cell().Element(HeadCell).Text("Beurteilung").SemiBold();
                        h.Cell().Element(HeadCell).Text("Begründung").SemiBold();
                    });
                    foreach (var (a, s) in rows)
                    {
                        var res = InfiltrationService.Assess(s.KfWertMs);
                        (string text, string col2, string grund) = res switch
                        {
                            InfiltrationResult.Geeignet => ("Geeignet", "#3d7a4f", $"kf zwischen {Limit(InfiltrationService.KfMin)} und {Limit(InfiltrationService.KfMax)} m/s"),
                            InfiltrationResult.ZuGeringDurchlaessig => ("Zu gering durchlässig", "#946509", $"kf < {Limit(InfiltrationService.KfMin)} m/s"),
                            InfiltrationResult.ZuDurchlaessig => ("Zu durchlässig", "#946509", $"kf > {Limit(InfiltrationService.KfMax)} m/s"),
                            _ => ("Unbekannt", Muted, "kein kf-Wert"),
                        };
                        t.Cell().Element(BodyCell).Text(a.Bezeichnung).SemiBold();
                        t.Cell().Element(BodyCell).Text($"{F(s.VonM, 2)}–{F(s.BisM, 2)} · {s.Kurzzeichen}");
                        t.Cell().Element(BodyCell).AlignRight().Text(KfModel.KfText(s.KfWertMs));
                        t.Cell().Element(BodyCell).Text(text).SemiBold().FontColor(col2);
                        t.Cell().Element(BodyCell).Text(grund).FontColor(Muted);
                    }
                });
                col.Item().PaddingTop(8).Text(VersickerungHinweis).SemiBold();
                col.Item().PaddingTop(3).Text($"Als Bereich für die Versickerung wird vereinfacht {Limit(InfiltrationService.KfMin)} bis {Limit(InfiltrationService.KfMax)} m/s angesetzt (frühere Regel A 138, 2005). Das aktuelle Arbeitsblatt DWA-A 138-1 arbeitet mit der Bemessungs-Infiltrationsrate; sie ist hier nicht ermittelt.")
                    .FontSize(8.5f).FontColor(Muted);
            });
        }
    }
}
