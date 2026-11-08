using System.Globalization;
using BoreholeHub.Core.Entities;
using BoreholeHub.Web.Data;
using BoreholeHub.Web.Drawing;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using Microsoft.EntityFrameworkCore;

namespace BoreholeHub.Web.Pages.Projekte;

public class ProfilschnittModel(BoreholeDbContext db) : PageModel
{
    private static readonly CultureInfo De = CultureInfo.GetCultureInfo("de-DE");

    public const int MinPunkte = 2;
    public const int MaxPunkte = 4;

    /// <summary>One row of the data table below the drawing.</summary>
    public record TabellenZeile(string Bezeichnung, string Abstand, string Ansatz, string Endteufe, string Grundwasser, string GrundwasserNhn, string Homogenbereiche);

    public Project Projekt { get; private set; } = null!;

    /// <summary>All Kleinrammbohrungen of the project in list order (offered as checkboxes).</summary>
    public IReadOnlyList<Aufschluss> Kleinrammbohrungen { get; private set; } = [];

    /// <summary>Ids of the points that are drawn, in drawing order.</summary>
    public IReadOnlyList<int> Auswahl { get; private set; } = [];

    /// <summary>Friendly message when the "punkte" parameter was not usable; the default section is shown instead.</summary>
    public string? Hinweis { get; private set; }

    public string? SchnittSvgHtml { get; private set; }

    public string? LageplanSvgHtml { get; private set; }

    public IReadOnlyList<TabellenZeile> Tabelle { get; private set; } = [];

    public string? KeinSchnittText { get; private set; }

    public async Task<IActionResult> OnGetAsync(int id)
    {
        // "punkte=1,3,5" and the repeated form fields "punkte=1&punkte=3&punkte=5" are both accepted.
        string punkte = string.Join(",", Request.Query["punkte"].ToArray());
        var projekt = await db.Projects.AsNoTracking().FirstOrDefaultAsync(p => p.Id == id);
        if (projekt is null)
        {
            return NotFound();
        }

        Projekt = projekt;
        var alle = await db.Aufschluesse.AsNoTracking()
            .Where(a => a.ProjectId == id)
            .Include(a => a.Schichten)
            .AsSplitQuery()
            .OrderBy(a => a.Id)
            .ToListAsync();
        Kleinrammbohrungen = alle.Where(a => a.Art == AufschlussArt.Kleinrammbohrung).ToList();

        if (Kleinrammbohrungen.Count < MinPunkte)
        {
            KeinSchnittText = "Für einen Profilschnitt werden mindestens zwei Kleinrammbohrungen im Projekt benötigt.";
            LageplanSvgHtml = ProfilschnittLageplan.Render(alle, []);
            return Page();
        }

        List<Aufschluss> auswahl;
        if (string.IsNullOrWhiteSpace(punkte))
        {
            auswahl = StandardAuswahl(Kleinrammbohrungen);
        }
        else if (TryParse(punkte, Kleinrammbohrungen, out var gewaehlt, out var fehler))
        {
            auswahl = gewaehlt;
        }
        else
        {
            Hinweis = fehler + " Gezeigt wird der Standardschnitt.";
            auswahl = StandardAuswahl(Kleinrammbohrungen);
        }

        Auswahl = auswahl.Select(a => a.Id).ToList();
        var schichten = auswahl.ToDictionary(a => a.Id, a => (IReadOnlyList<Schicht>)a.Schichten.OrderBy(s => s.VonM).ToList());
        SchnittSvgHtml = ProfilschnittSvg.Render(auswahl, schichten);
        LageplanSvgHtml = ProfilschnittLageplan.Render(alle, auswahl);
        Tabelle = BuildTabelle(auswahl);
        return Page();
    }

    /// <summary>
    /// Parses a comma-separated id list. Accepts only 2 to 4 different Kleinrammbohrungen of the project.
    /// </summary>
    public static bool TryParse(string text, IReadOnlyList<Aufschluss> kandidaten, out List<Aufschluss> ergebnis, out string fehler)
    {
        ergebnis = [];
        fehler = string.Empty;
        var teile = text.Split(',', StringSplitOptions.TrimEntries | StringSplitOptions.RemoveEmptyEntries);
        var ids = new List<int>();
        foreach (var t in teile)
        {
            if (!int.TryParse(t, NumberStyles.None, CultureInfo.InvariantCulture, out int v))
            {
                fehler = $"„{t}“ ist keine gültige Kennung eines Aufschlusses.";
                return false;
            }

            ids.Add(v);
        }

        if (ids.Count != ids.Distinct().Count())
        {
            fehler = "Jede Bohrung kann nur einmal im Schnitt vorkommen.";
            return false;
        }

        if (ids.Count < MinPunkte || ids.Count > MaxPunkte)
        {
            fehler = $"Bitte {MinPunkte} bis {MaxPunkte} Kleinrammbohrungen wählen.";
            return false;
        }

        foreach (int v in ids)
        {
            var a = kandidaten.FirstOrDefault(k => k.Id == v);
            if (a is null)
            {
                fehler = $"Die Kennung {v} gehört zu keiner Kleinrammbohrung dieses Projekts.";
                return false;
            }

            ergebnis.Add(a);
        }

        return true;
    }

    /// <summary>
    /// Default section: the two Kleinrammbohrungen with the largest distance (ties: lowest ids), plus the one lying closest
    /// to the line between them (if any lies between them), ordered along the line.
    /// </summary>
    public static List<Aufschluss> StandardAuswahl(IReadOnlyList<Aufschluss> krb)
    {
        var list = krb.OrderBy(a => a.Id).ToList();
        if (list.Count <= 2)
        {
            return list;
        }

        (Aufschluss A, Aufschluss B, double D)? best = null;
        for (int i = 0; i < list.Count; i++)
        {
            for (int j = i + 1; j < list.Count; j++)
            {
                double d = ProfilschnittSvg.Dist(list[i], list[j]);
                if (best is null || d > best.Value.D + 1e-9)
                {
                    best = (list[i], list[j], d);
                }
            }
        }

        var (p, q, len) = best!.Value;
        double dx = q.Rechtswert - p.Rechtswert, dy = q.Hochwert - p.Hochwert;
        Aufschluss? mid = null;
        double midOff = double.MaxValue;
        foreach (var c in list.Where(c => c.Id != p.Id && c.Id != q.Id))
        {
            double t = len <= 0 ? 0 : ((c.Rechtswert - p.Rechtswert) * dx + (c.Hochwert - p.Hochwert) * dy) / (len * len);
            if (t <= 0.1 || t >= 0.9) { continue; }
            double off = Math.Abs((c.Rechtswert - p.Rechtswert) * dy - (c.Hochwert - p.Hochwert) * dx) / len;
            if (off < midOff - 1e-9) { mid = c; midOff = off; }
        }

        return mid is null ? [p, q] : [p, mid, q];
    }

    private List<TabellenZeile> BuildTabelle(List<Aufschluss> auswahl)
    {
        var rows = new List<TabellenZeile>();
        double station = 0;
        for (int i = 0; i < auswahl.Count; i++)
        {
            var a = auswahl[i];
            if (i > 0) { station += ProfilschnittSvg.Dist(auswahl[i - 1], a); }
            var hb = string.Join(", ", a.Schichten.OrderBy(s => s.VonM)
                .GroupBy(s => string.IsNullOrWhiteSpace(s.Homogenbereich) ? "–" : s.Homogenbereich!)
                .Select(g => $"{g.Key} {(g.Min(s => s.VonM)).ToString("0.0", De)}–{(g.Max(s => s.BisM)).ToString("0.0", De)}"));
            rows.Add(new TabellenZeile(
                a.Bezeichnung,
                station.ToString("0.0", De),
                a.AnsatzhoeheMNhn.ToString("0.00", De),
                a.EndteufeM.ToString("0.00", De),
                a.GrundwasserM is double g ? g.ToString("0.00", De) : "–",
                a.GrundwasserM is double g2 ? (a.AnsatzhoeheMNhn - g2).ToString("0.00", De) : "–",
                hb));
        }

        return rows;
    }
}
