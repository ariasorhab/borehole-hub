using System.Text;
using BoreholeHub.Core.Entities;
using BoreholeHub.Web.Import;

namespace BoreholeHub.Tests;

public class CsvImportServiceTests
{
    private const string AH = CsvImportService.AufschluesseHeader;
    private const string SH = CsvImportService.SchichtenHeader;

    private static readonly CsvImportService Service = new();

    private static string A(params string[] rows) => AH + "\r\n" + string.Join("\r\n", rows) + "\r\n";

    private static string S(params string[] rows) => SH + "\r\n" + string.Join("\r\n", rows) + "\r\n";

    private static string Row(string name = "KRB 6", string art = "KRB", string rw = "516296,5", string hw = "5640626", string h = "212,8", string et = "3,0", string datum = "27.10.2026", string gw = "")
        => string.Join(";", name, art, rw, hw, h, et, datum, gw);

    private static string Layer(string name, string von, string bis, string kurz = "U, t", string beschr = "Auenlehm", string gruppe = "TL", string farbe = "braun", string zustand = "steif", string hb = "B2", string frost = "F3", string kf = "5E-08")
        => string.Join(";", name, von, bis, kurz, beschr, gruppe, farbe, zustand, hb, frost, kf);

    private static ImportResult Parse(string? a, string? s, params Aufschluss[] existing) => Service.Parse(a, s, existing);

    private static void AssertError(ImportResult r, string expected)
    {
        Assert.Contains(expected, r.Fehler);
        Assert.Empty(r.NeueAufschluesse);
        Assert.Empty(r.NeueSchichten);
    }

    [Fact]
    public void ValidUpload_TwoAufschluesse_WithLayers_Imports()
    {
        var r = Parse(
            A(Row("KRB 6"), Row("DPH 4", art: "Rammsondierung", et: "4,0", gw: "2,9")),
            S(Layer("KRB 6", "0,00", "0,30", "U, h, s'", "Mutterboden", "OH", kf: "1E-06"), Layer("KRB 6", "0,30", "3,00", kf: "5,0E-08")));

        Assert.Empty(r.Fehler);
        Assert.Equal(2, r.NeueAufschluesse.Count);
        Assert.Equal(2, r.NeueSchichten.Count);
        var krb = r.NeueAufschluesse[0];
        Assert.Equal(AufschlussArt.Kleinrammbohrung, krb.Art);
        Assert.Equal(516296.5, krb.Rechtswert);
        Assert.Equal(212.8, krb.AnsatzhoeheMNhn);
        Assert.Equal(new DateOnly(2026, 10, 27), krb.Datum);
        Assert.Null(krb.GrundwasserM);
        Assert.Equal(AufschlussArt.Rammsondierung, r.NeueAufschluesse[1].Art);
        Assert.Equal(2.9, r.NeueAufschluesse[1].GrundwasserM);
        Assert.All(r.NeueSchichten, s => Assert.Same(krb, s.Aufschluss));
        Assert.Equal(1e-6, r.NeueSchichten[0].KfWertMs);
        Assert.Equal(5e-8, r.NeueSchichten[1].KfWertMs);
        Assert.Equal("F3", r.NeueSchichten[1].Frostklasse);
    }

    [Fact]
    public void BomAndDecimalComma_AreHandled()
    {
        var bytes = new UTF8Encoding(true).GetPreamble().Concat(Encoding.UTF8.GetBytes(A(Row(rw: "516296,5", h: "212,85")))).ToArray();
        string text = CsvImportService.Decode(bytes);
        var r = Parse(text, null);

        Assert.DoesNotContain('﻿', text);
        Assert.Empty(r.Fehler);
        Assert.Equal(212.85, r.NeueAufschluesse.Single().AnsatzhoeheMNhn);
    }

    [Fact]
    public void Decode_FallsBackToLatin1_ForOldExcelFiles()
    {
        string text = CsvImportService.Decode(Encoding.Latin1.GetBytes("Mutterboden; Schluff, schwach sandig Übergang"));
        Assert.Contains("Übergang", text);
    }

    [Fact]
    public void KfFormats_AreAccepted()
    {
        foreach (var (kf, expected) in new[] { ("5E-08", 5e-8), ("5,0E-08", 5e-8), ("0,00000005", 5e-8), ("", 0.0) })
        {
            var r = Parse(A(Row()), S(Layer("KRB 6", "0", "3,0", kf: kf)));
            Assert.Empty(r.Fehler);
            if (kf.Length == 0) { Assert.Null(r.NeueSchichten.Single().KfWertMs); }
            else { Assert.Equal(expected, r.NeueSchichten.Single().KfWertMs!.Value, 12); }
        }
    }

    [Fact]
    public void LayerGap_IsReported_WithLineNumber_AndNothingIsReturned()
    {
        var r = Parse(A(Row("KRB 6")), S(Layer("KRB 6", "0,00", "1,20"), Layer("KRB 6", "1,40", "3,00")));
        AssertError(r, "Schichten, Zeile 3: Lücke zwischen 1,20 m und 1,40 m bei KRB 6");
    }

    [Fact]
    public void LayerOverlap_IsReported()
    {
        var r = Parse(A(Row("KRB 6")), S(Layer("KRB 6", "0,00", "1,50"), Layer("KRB 6", "1,40", "3,00")));
        AssertError(r, "Schichten, Zeile 3: Überlappung zwischen 1,40 m und 1,50 m bei KRB 6");
    }

    [Fact]
    public void FirstLayerNotAtZero_IsReported()
    {
        var r = Parse(A(Row("KRB 6")), S(Layer("KRB 6", "0,20", "3,00")));
        AssertError(r, "Schichten, Zeile 2: Die erste Schicht von KRB 6 beginnt bei 0,20 m, nicht bei 0,00 m.");
    }

    [Fact]
    public void LastLayerNotAtEndteufe_IsReported()
    {
        var r = Parse(A(Row("KRB 6", et: "3,0")), S(Layer("KRB 6", "0,00", "2,50")));
        AssertError(r, "Schichten, Zeile 2: Die letzte Schicht von KRB 6 endet bei 2,50 m, die Endteufe ist 3,00 m.");
    }

    [Fact]
    public void DuplicateBezeichnung_InProject_IsReported()
    {
        var vorhanden = new Aufschluss { Id = 5, Bezeichnung = "KRB 6", EndteufeM = 3 };
        var r = Parse(A(Row("KRB 6")), null, vorhanden);
        AssertError(r, "Aufschlüsse, Zeile 2: Bezeichnung \"KRB 6\" existiert bereits im Projekt.");
    }

    [Fact]
    public void DuplicateBezeichnung_InFile_IsReported()
    {
        var r = Parse(A(Row("KRB 6"), Row("krb 6")), null);
        AssertError(r, "Aufschlüsse, Zeile 3: Bezeichnung \"krb 6\" kommt in dieser Datei mehrfach vor (zuerst in Zeile 2).");
    }

    [Fact]
    public void InvalidArt_IsReported()
    {
        var r = Parse(A(Row(art: "Schurf")), null);
        AssertError(r, "Aufschlüsse, Zeile 2: Ungültige Art \"Schurf\" (erlaubt: KRB, DPH, Kleinrammbohrung, Rammsondierung).");
    }

    [Fact]
    public void InvalidDate_IsReported()
    {
        var r = Parse(A(Row(datum: "2026-10-27")), null);
        AssertError(r, "Aufschlüsse, Zeile 2: Ungültiges Datum \"2026-10-27\" (Format TT.MM.JJJJ).");
    }

    [Fact]
    public void DateBeforeMinDatum_IsReported()
    {
        var r = Parse(A(Row(datum: "31.12.1999")), null);
        AssertError(r, "Aufschlüsse, Zeile 2: Datum \"31.12.1999\" liegt außerhalb von 01.01.2000 bis " + CsvImportService.MaxDatum().ToString("dd.MM.yyyy", System.Globalization.CultureInfo.InvariantCulture) + ".");
    }

    [Fact]
    public void DateOnMinDatum_IsAccepted()
    {
        var r = Parse(A(Row(datum: "01.01.2000")), null);
        Assert.Empty(r.Fehler);
        Assert.Equal(new DateOnly(2000, 1, 1), r.NeueAufschluesse.Single().Datum);
    }

    [Fact]
    public void DateOnMaxDatum_IsAccepted_AndOneDayLaterIsReported()
    {
        var max = CsvImportService.MaxDatum();
        var ok = Parse(A(Row(datum: max.ToString("dd.MM.yyyy", System.Globalization.CultureInfo.InvariantCulture))), null);
        Assert.Empty(ok.Fehler);

        var later = max.AddDays(1).ToString("dd.MM.yyyy", System.Globalization.CultureInfo.InvariantCulture);
        var bad = Parse(A(Row(datum: later)), null);
        AssertError(bad, "Aufschlüsse, Zeile 2: Datum \"" + later + "\" liegt außerhalb von 01.01.2000 bis " + max.ToString("dd.MM.yyyy", System.Globalization.CultureInfo.InvariantCulture) + ".");
    }

    [Fact]
    public void InvalidNumber_AndMissingValue_AreReported()
    {
        var r = Parse(A(Row(rw: "abc", et: "")), null);
        Assert.Contains("Aufschlüsse, Zeile 2: Rechtswert \"abc\" ist keine Zahl.", r.Fehler);
        Assert.Contains("Aufschlüsse, Zeile 2: Endteufe_m fehlt.", r.Fehler);
        Assert.Empty(r.NeueAufschluesse);
    }

    [Fact]
    public void GroundwaterBelowEndteufe_IsReported()
    {
        var r = Parse(A(Row(et: "3,0", gw: "3,5")), null);
        AssertError(r, "Aufschlüsse, Zeile 2: Grundwasser_m (3,50 m) liegt tiefer als die Endteufe (3,00 m).");
    }

    [Fact]
    public void WrongHeader_AndWrongColumnCount_AreReported()
    {
        var r1 = Parse("Name;Art\r\nKRB 6;KRB\r\n", null);
        Assert.Contains("Kopfzeile stimmt nicht", r1.Fehler.Single());
        Assert.StartsWith("Aufschlüsse, Zeile 1:", r1.Fehler.Single());

        var r2 = Parse(AH + "\r\nKRB 6;KRB;516296\r\n", null);
        Assert.Contains("Aufschlüsse, Zeile 2: 8 Spalten erwartet, 3 gefunden", r2.Fehler.Single());
    }

    [Fact]
    public void InvalidFrostklasse_AndUnknownAufschluss_AreReported()
    {
        var r = Parse(A(Row("KRB 6")), S(Layer("KRB 6", "0", "3,0", frost: "F4"), Layer("KRB 99", "0", "1,0")));
        Assert.Contains("Schichten, Zeile 2: Ungültige Frostklasse \"F4\" (erlaubt: F1, F2, F3 oder leer).", r.Fehler);
        Assert.Contains("Schichten, Zeile 3: Aufschluss \"KRB 99\" ist weder in der Aufschlüsse-Datei noch im Projekt vorhanden.", r.Fehler);
    }

    [Fact]
    public void LayersForExistingAufschluss_UseItsId_AndRespectItsEndteufe()
    {
        var vorhanden = new Aufschluss { Id = 7, Bezeichnung = "KRB 1", EndteufeM = 2.0, Art = AufschlussArt.Kleinrammbohrung };
        var ok = Parse(null, S(Layer("KRB 1", "0", "2,0")), vorhanden);
        Assert.Empty(ok.Fehler);
        Assert.Empty(ok.NeueAufschluesse);
        Assert.Equal(7, ok.NeueSchichten.Single().AufschlussId);

        var bad = Parse(null, S(Layer("KRB 1", "0", "1,5")), vorhanden);
        AssertError(bad, "Schichten, Zeile 2: Die letzte Schicht von KRB 1 endet bei 1,50 m, die Endteufe ist 2,00 m.");
    }

    [Fact]
    public void NoFiles_IsReported()
    {
        var r = Parse(null, "  ");
        Assert.Single(r.Fehler);
        Assert.Contains("keine Datei", r.Fehler[0]);
    }

    [Fact]
    public void MissingTrailingNewline_AndBlankLines_AreHandled()
    {
        var r = Parse(AH + "\r\n\r\n" + Row("KRB 6"), null);
        Assert.Empty(r.Fehler);
        Assert.Single(r.NeueAufschluesse);
    }
}
