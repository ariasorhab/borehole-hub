using System.Net;
using System.Text;
using System.Text.RegularExpressions;
using BoreholeHub.Core.Entities;
using BoreholeHub.Web.Data;
using BoreholeHub.Web.Drawing;
using BoreholeHub.Web.Reports;
using Microsoft.EntityFrameworkCore;

namespace BoreholeHub.Tests;

public class ProjektBerichtPdfTests : IClassFixture<WebAppFactory>
{
    private readonly WebAppFactory _factory;

    public ProjektBerichtPdfTests(WebAppFactory factory) => _factory = factory;

    private async Task<int> SeedAsync()
    {
        int id = 0;
        await _factory.WithDbAsync(async db => id = await DemoDataSeeder.SeedAsync(db));
        return id;
    }

    private static int CountPages(byte[] pdf)
    {
        // Page dictionaries are written in clear text ("/Type /Page", the page tree is "/Type /Pages").
        string text = Encoding.Latin1.GetString(pdf);
        return Regex.Matches(text, @"/Type\s*/Page(?![A-Za-z])").Count;
    }

    [Fact]
    public async Task PdfHandler_ReturnsPdf_WithFileName_AndAtLeastTenPages()
    {
        int id = await SeedAsync();
        var response = await _factory.CreateClient().GetAsync($"/Projekte/{id}?handler=Pdf");
        var bytes = await response.Content.ReadAsByteArrayAsync();

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Equal("application/pdf", response.Content.Headers.ContentType?.MediaType);
        Assert.Equal("%PDF", Encoding.ASCII.GetString(bytes, 0, 4));
        Assert.Equal("neubau-einfamilienhaus-mit-garage-schwalmstadt-ziegenhain-demo-anlagen.pdf",
            response.Content.Headers.ContentDisposition?.FileNameStar ?? response.Content.Headers.ContentDisposition?.FileName);

        // Deckblatt + Lageplan + 5 Bohrprofile + Profilschnitt + Labor + Versickerung = at least 10 pages.
        Assert.True(CountPages(bytes) >= 1 + 1 + 5 + 1 + 1 + 1, $"pages: {CountPages(bytes)}");
    }

    [Fact]
    public async Task PdfHandler_UnknownProject_Returns404()
    {
        var response = await _factory.CreateClient().GetAsync("/Projekte/99999?handler=Pdf");
        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
    }

    [Fact]
    public async Task DetailsPage_HasPdfButton()
    {
        int id = await SeedAsync();
        var html = await _factory.CreateClient().GetStringAsync($"/Projekte/{id}");
        Assert.Contains($"href=\"/Projekte/{id}?handler=Pdf\">PDF-Bericht</a>", html);
        Assert.Contains("btn btn-secondary", html);
    }

    [Fact]
    public async Task Erzeugen_WithOneArgument_BuildsReportFromLoadedProject()
    {
        int id = await SeedAsync();
        Project? projekt = null;
        await _factory.WithDbAsync(async db =>
            projekt = await db.Projects.AsNoTracking()
                .Include(p => p.Aufschluesse).ThenInclude(a => a.Schichten)
                .Include(p => p.Aufschluesse).ThenInclude(a => a.Proben).ThenInclude(pr => pr.Laborwerte)
                .Include(p => p.Aufschluesse).ThenInclude(a => a.Messwerte)
                .AsSplitQuery().FirstAsync(p => p.Id == id));

        byte[] pdf = ProjektBerichtPdf.Erzeugen(projekt!);

        Assert.Equal("%PDF", Encoding.ASCII.GetString(pdf, 0, 4));
        Assert.True(CountPages(pdf) >= 10);
    }

    [Fact]
    public void Dateiname_IsSlug_WithGermanUmlauts()
    {
        Assert.Equal("gruenes-haus-strasse-12-anlagen.pdf", ProjektBerichtPdf.Dateiname("Grünes Haus – Straße 12 (Demo)").Replace("-demo", string.Empty));
        Assert.Equal("projekt-anlagen.pdf", ProjektBerichtPdf.Dateiname("***"));
    }

    [Fact]
    public void SvgPrint_ReplacesClassesAndPatterns_ForPdf_WithoutChangingWebOutput()
    {
        var a = new Aufschluss { Id = 1, Bezeichnung = "KRB 1", Art = AufschlussArt.Kleinrammbohrung, EndteufeM = 2, Datum = new DateOnly(2026, 1, 1) };
        a.Schichten.Add(new Schicht { VonM = 0, BisM = 2, Kurzzeichen = "G, s", Beschreibung = "Kies", Bodengruppe = "GW", Homogenbereich = "B1" });
        string web = BohrprofilSvg.Render(a, a.Schichten, 2);
        string print = SvgPrint.Apply(web);

        Assert.Contains("class=\"dr-title\"", web);
        Assert.Contains("<pattern", web);
        Assert.DoesNotContain("class=\"dr-", print);
        Assert.DoesNotContain("<pattern", print);
        Assert.DoesNotContain("currentColor", print);
        Assert.DoesNotContain("var(--", print);
        Assert.Contains("<clipPath", print);
    }
}
