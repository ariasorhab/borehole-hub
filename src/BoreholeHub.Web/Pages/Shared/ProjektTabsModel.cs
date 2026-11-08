namespace BoreholeHub.Web.Pages.Shared;

/// <summary>Model of the project tab bar. <paramref name="Aktiv"/> is "uebersicht", "karte", "labor" or "daten".</summary>
public record ProjektTabsModel(int ProjektId, string Aktiv);
