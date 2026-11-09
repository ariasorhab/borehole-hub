using BoreholeHub.Core.Entities;
using BoreholeHub.Core.Services;

namespace BoreholeHub.Tests;

public class DataQualityServiceTests
{
    // ---------- helpers ----------

    private static Schicht S(double von, double bis, string? bodengruppe = "TL", double? kf = null) => new()
    {
        VonM = von, BisM = bis, Kurzzeichen = "U, t", Beschreibung = "Test", Bodengruppe = bodengruppe, KfWertMs = kf,
    };

    private static Probe P(string bezeichnung, double von, double bis) => new() { Bezeichnung = bezeichnung, VonM = von, BisM = bis };

    private static RammMesswert M(double tiefe, int n10 = 5) => new() { TiefeBisM = tiefe, N10 = n10 };

    /// <summary>A valid Kleinrammbohrung in the middle of Hessen, at 200 m NHN.</summary>
    private static Aufschluss Krb(int id, string bezeichnung, double endteufe, params Schicht[] schichten) => new()
    {
        Id = id,
        Bezeichnung = bezeichnung,
        Art = AufschlussArt.Kleinrammbohrung,
        Rechtswert = 516000,
        Hochwert = 5640000,
        AnsatzhoeheMNhn = 200,
        EndteufeM = endteufe,
        Datum = new DateOnly(2026, 11, 1),
        Schichten = [.. schichten],
    };

    private static Aufschluss Dph(int id, string bezeichnung, double endteufe, params RammMesswert[] messwerte) => new()
    {
        Id = id,
        Bezeichnung = bezeichnung,
        Art = AufschlussArt.Rammsondierung,
        Rechtswert = 516000,
        Hochwert = 5640000,
        AnsatzhoeheMNhn = 200,
        EndteufeM = endteufe,
        Datum = new DateOnly(2026, 11, 1),
        Messwerte = [.. messwerte],
    };

    private static IReadOnlyList<PruefBefund> Pruefen(params Aufschluss[] aufschluesse) =>
        DataQualityService.Pruefen(aufschluesse);

    private static PruefBefund Single(IReadOnlyList<PruefBefund> befunde, PruefStufe stufe, string meldung)
    {
        var treffer = befunde.Where(b => b.Stufe == stufe && b.Meldung == meldung).ToList();
        Assert.Single(treffer);
        return treffer[0];
    }

    // ---------- example from the specification ----------

    [Fact]
    public void Beispiel_KRB9_LueckeInDenSchichten_ErgibtGenauEinenBefund()
    {
        var krb9 = Krb(9, "KRB 9", 3.0, S(0.0, 1.0), S(1.2, 3.0));

        var befund = Assert.Single(Pruefen(krb9));

        Assert.Equal(new PruefBefund("Schichtfolge", PruefStufe.Fehler, 9, "KRB 9", "Lücke 1,00–1,20 m."), befund);
    }

    // ---------- clean data ----------

    [Fact]
    public void SaubereDaten_ErgebenKeineBefunde()
    {
        var krb1 = Krb(1, "KRB 1", 3.0, S(0.0, 1.0, "TL"), S(1.0, 3.0, "SE", 1e-6));
        krb1.Proben = [P("KRB 1/1", 0.2, 0.8)];
        var krb2 = Krb(2, "KRB 2", 3.0, S(0.0, 3.0, "UL"));
        krb2.AnsatzhoeheMNhn = 201;
        krb2.Rechtswert = 516050;
        var dph1 = Dph(3, "DPH 1", 0.3, M(0.1), M(0.2), M(0.3));
        dph1.Rechtswert = 516100;
        dph1.AnsatzhoeheMNhn = 199;

        Assert.Empty(Pruefen(krb1, krb2, dph1));
    }

    [Fact]
    public void LeereListe_ErgibtKeineBefunde()
    {
        Assert.Empty(DataQualityService.Pruefen([]));
    }

    // ---------- R1 Schichtfolge ----------

    [Fact]
    public void R1_KeineSchichten_IstFehler()
    {
        var befund = Assert.Single(Pruefen(Krb(1, "KRB 1", 3.0)));

        Assert.Equal(PruefStufe.Fehler, befund.Stufe);
        Assert.Equal("Schichtfolge", befund.Regel);
        Assert.Equal("Keine Schichten erfasst.", befund.Meldung);
    }

    [Fact]
    public void R1_UmgekehrteSchicht_IstFehler()
    {
        var befunde = Pruefen(Krb(1, "KRB 1", 1.0, S(0.0, 1.0), S(1.0, 0.5)));

        Single(befunde, PruefStufe.Fehler, "Schicht 1,00–0,50: Oberkante liegt nicht über Unterkante.");
    }

    [Fact]
    public void R1_LueckeAmAnfang_IstFehler()
    {
        var befunde = Pruefen(Krb(1, "KRB 1", 3.0, S(0.2, 3.0)));

        Single(befunde, PruefStufe.Fehler, "Lücke 0,00–0,20 m.");
    }

    [Fact]
    public void R1_Ueberlappung_IstFehler()
    {
        var befunde = Pruefen(Krb(1, "KRB 1", 3.0, S(0.0, 1.2), S(1.0, 3.0)));

        Single(befunde, PruefStufe.Fehler, "Überlappung 1,00–1,20 m.");
    }

    [Fact]
    public void R1_LetzteSchichtKurzVorEndteufe_IstHinweis()
    {
        var befund = Assert.Single(Pruefen(Krb(1, "KRB 1", 3.0, S(0.0, 1.0), S(1.0, 2.5))));

        Assert.Equal(PruefStufe.Hinweis, befund.Stufe);
        Assert.Equal("Letzte Schicht endet bei 2,50 m, Endteufe 3,00 m.", befund.Meldung);
    }

    [Fact]
    public void R1_SchichtenWerdenNachVonMSortiert_BevorGeprueftWird()
    {
        // Listed out of order, but correct once sorted by VonM.
        var befunde = Pruefen(Krb(1, "KRB 1", 3.0, S(1.0, 3.0), S(0.0, 1.0)));

        Assert.Empty(befunde);
    }

    // ---------- R2 Endteufe ----------

    [Fact]
    public void R2_EndteufeNull_IstFehler()
    {
        var befunde = Pruefen(Krb(1, "KRB 1", 0.0, S(0.0, 1.0)));

        Single(befunde, PruefStufe.Fehler, "Endteufe fehlt oder ist 0.");
    }

    [Fact]
    public void R2_EndteufeUeberVierMeter_IstHinweis()
    {
        var befund = Assert.Single(Pruefen(Krb(1, "KRB 1", 4.5, S(0.0, 4.5))));

        Assert.Equal(new PruefBefund("Endteufe", PruefStufe.Hinweis, 1, "KRB 1", "Endteufe 4,50 m liegt über 4,00 m (Rahmen GK1)."), befund);
    }

    // ---------- R3 Proben ----------

    [Fact]
    public void R3_ProbeUmgekehrt_IstFehler()
    {
        var krb = Krb(1, "KRB 1", 3.0, S(0.0, 3.0));
        krb.Proben = [P("KRB 1/1", 1.0, 0.5)];

        Single(Pruefen(krb), PruefStufe.Fehler, "Probe KRB 1/1: Oberkante liegt nicht über Unterkante.");
    }

    [Fact]
    public void R3_ProbeAusserhalbDerBohrung_IstFehler()
    {
        var krb = Krb(1, "KRB 1", 3.0, S(0.0, 3.0));
        krb.Proben = [P("KRB 1/2", 2.0, 3.5)];

        Single(Pruefen(krb), PruefStufe.Fehler, "Probe KRB 1/2 (2,00–3,50 m) liegt außerhalb der Bohrung.");
    }

    [Fact]
    public void R3_ProbeUeberSchichtgrenze_IstHinweis()
    {
        var krb = Krb(1, "KRB 1", 3.0, S(0.0, 1.0), S(1.0, 3.0));
        krb.Proben = [P("KRB 1/3", 0.8, 1.3)];

        var befund = Assert.Single(Pruefen(krb));

        Assert.Equal(new PruefBefund("Proben", PruefStufe.Hinweis, 1, "KRB 1", "Probe KRB 1/3 (0,80–1,30 m) reicht über eine Schichtgrenze."), befund);
    }

    // ---------- R4 Koordinaten ----------

    [Fact]
    public void R4_KoordinatenAusserhalbHessens_IstFehler()
    {
        var krb = Krb(1, "KRB 1", 3.0, S(0.0, 3.0));
        krb.Rechtswert = 300000;

        var befund = Assert.Single(Pruefen(krb));

        Assert.Equal(new PruefBefund("Koordinaten", PruefStufe.Fehler, 1, "KRB 1", "Koordinaten liegen außerhalb Hessens (ETRS89 / UTM 32N)."), befund);
    }

    [Fact]
    public void R4_WeitVomProjektmittelpunkt_IstHinweisFuerJedenPunkt()
    {
        var a = Krb(1, "KRB 1", 3.0, S(0.0, 3.0));
        var b = Krb(2, "KRB 2", 3.0, S(0.0, 3.0));
        b.Rechtswert = 517200; // mean is 516600: both points are 600 m away

        var befunde = Pruefen(a, b);

        Assert.Equal(2, befunde.Count);
        Assert.All(befunde, f =>
        {
            Assert.Equal(PruefStufe.Hinweis, f.Stufe);
            Assert.Equal("Koordinaten", f.Regel);
            Assert.Equal("Liegt 600 m vom Projektmittelpunkt entfernt.", f.Meldung);
        });
    }

    [Fact]
    public void R4_AbstandWirdNurBeiZweiGueltigenPunktenGeprueft()
    {
        var gueltig = Krb(1, "KRB 1", 3.0, S(0.0, 3.0));
        var ungueltig = Krb(2, "KRB 2", 3.0, S(0.0, 3.0));
        ungueltig.Hochwert = 5000000;

        var befunde = Pruefen(gueltig, ungueltig);

        var befund = Assert.Single(befunde);
        Assert.Equal(PruefStufe.Fehler, befund.Stufe);
        Assert.Equal(2, befund.AufschlussId);
    }

    // ---------- R5 Ansatzhöhe ----------

    [Fact]
    public void R5_AnsatzhoeheUnplausibel_IstFehler()
    {
        var krb = Krb(1, "KRB 1", 3.0, S(0.0, 3.0));
        krb.AnsatzhoeheMNhn = 30;

        var befund = Assert.Single(Pruefen(krb));

        Assert.Equal(new PruefBefund("Ansatzhöhe", PruefStufe.Fehler, 1, "KRB 1", "Ansatzhöhe 30,00 m NHN ist für Hessen nicht plausibel."), befund);
    }

    [Fact]
    public void R5_AbweichungVomMedian_IstHinweis()
    {
        var a = Krb(1, "KRB 1", 3.0, S(0.0, 3.0));
        var b = Krb(2, "KRB 2", 3.0, S(0.0, 3.0));
        var c = Krb(3, "KRB 3", 3.0, S(0.0, 3.0));
        a.AnsatzhoeheMNhn = 200;
        b.AnsatzhoeheMNhn = 201;
        c.AnsatzhoeheMNhn = 230; // median 201, deviation 29

        var befund = Assert.Single(Pruefen(a, b, c));

        Assert.Equal(new PruefBefund("Ansatzhöhe", PruefStufe.Hinweis, 3, "KRB 3", "Ansatzhöhe weicht 29,00 m vom Median des Projekts ab."), befund);
    }

    [Fact]
    public void R5_MedianWirdErstAbDreiPlausiblenHoehenGeprueft()
    {
        var a = Krb(1, "KRB 1", 3.0, S(0.0, 3.0));
        var b = Krb(2, "KRB 2", 3.0, S(0.0, 3.0));
        b.AnsatzhoeheMNhn = 300; // two heights only: no median check

        Assert.Empty(Pruefen(a, b));
    }

    // ---------- R6 Rammsondierung ----------

    [Fact]
    public void R6_KeineMesswerte_IstFehler()
    {
        var befund = Assert.Single(Pruefen(Dph(1, "DPH 1", 3.0)));

        Assert.Equal(new PruefBefund("Rammsondierung", PruefStufe.Fehler, 1, "DPH 1", "Keine Messwerte erfasst."), befund);
    }

    [Fact]
    public void R6_NegativeSchlagzahl_IstFehler()
    {
        var befunde = Pruefen(Dph(1, "DPH 1", 0.1, M(0.1, -1)));

        Single(befunde, PruefStufe.Fehler, "Negative Schlagzahl bei 0,10 m.");
    }

    [Fact]
    public void R6_SehrHoheSchlagzahl_IstHinweis()
    {
        var befunde = Pruefen(Dph(1, "DPH 1", 0.2, M(0.1), M(0.2, 60)));

        Single(befunde, PruefStufe.Hinweis, "Sehr hohe Schlagzahl N10 = 60 bei 0,20 m – Abbruchkriterium prüfen.");
    }

    [Fact]
    public void R6_LueckeInDerReihe_IstHinweisEinmalProStelle()
    {
        var befunde = Pruefen(Dph(1, "DPH 1", 0.4, M(0.1), M(0.3), M(0.4)));

        var befund = Assert.Single(befunde);
        Assert.Equal(new PruefBefund("Rammsondierung", PruefStufe.Hinweis, 1, "DPH 1", "Messwertreihe lückenhaft bei 0,30 m."), befund);
    }

    [Fact]
    public void R6_ErsterWertNichtBeiZehnCm_IstHinweis()
    {
        var befunde = Pruefen(Dph(1, "DPH 1", 0.3, M(0.2), M(0.3)));

        Single(befunde, PruefStufe.Hinweis, "Messwertreihe lückenhaft bei 0,20 m.");
    }

    // ---------- R7 Versickerung ----------

    [Theory]
    [InlineData("SE")]
    [InlineData("gu")]
    [InlineData(" GT ")]
    public void R7_VersickerungsBodengruppeOhneKf_IstHinweis(string bodengruppe)
    {
        var krb = Krb(1, "KRB 1", 1.0, S(0.0, 1.0, bodengruppe, null));

        var befund = Assert.Single(Pruefen(krb));

        Assert.Equal(PruefStufe.Hinweis, befund.Stufe);
        Assert.Equal("Versickerung", befund.Regel);
        Assert.Equal($"kf-Wert fehlt für Schicht 0,00–1,00 m ({bodengruppe.Trim()}) – Versickerung nicht prüfbar.", befund.Meldung);
    }

    [Fact]
    public void R7_NichtVersickerungsBodengruppeOderKfVorhanden_IstOhneBefund()
    {
        var krb = Krb(1, "KRB 1", 2.0, S(0.0, 1.0, "UL", null), S(1.0, 2.0, "SE", 1e-6));

        Assert.Empty(Pruefen(krb));
    }

    // ---------- order ----------

    [Fact]
    public void Reihenfolge_FehlerZuerst_DannBezeichnung_DannRegel()
    {
        // Zeta: endteufe 0 (Fehler R2) and last layer ends at 3,00 m (Hinweis R1).
        var zeta = Krb(1, "Zeta", 0.0, S(0.0, 3.0));
        // Alpha: endteufe 4,50 m (Hinweis R2) and last layer ends at 3,00 m (Hinweis R1).
        var alpha = Krb(2, "Alpha", 4.5, S(0.0, 3.0));

        var befunde = Pruefen(zeta, alpha);

        var reihenfolge = befunde.Select(b => (b.Stufe, b.Aufschluss, b.Regel)).ToList();
        var erwartet = new List<(PruefStufe Stufe, string Aufschluss, string Regel)>
        {
            (PruefStufe.Fehler, "Zeta", "Endteufe"),
            (PruefStufe.Hinweis, "Alpha", "Schichtfolge"),
            (PruefStufe.Hinweis, "Alpha", "Endteufe"),
            (PruefStufe.Hinweis, "Zeta", "Schichtfolge"),
        };
        Assert.Equal(erwartet, reihenfolge);
    }
}
