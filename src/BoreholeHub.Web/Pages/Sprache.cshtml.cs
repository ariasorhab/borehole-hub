using Microsoft.AspNetCore.Localization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;

namespace BoreholeHub.Web.Pages;

/// <summary>Sets the UI language cookie ("bh-lang") from the DE | EN switch in the header.</summary>
public class SpracheModel(IConfiguration configuration) : PageModel
{
    private bool Enabled => configuration.GetValue("Features:LanguageSwitch", false);

    public const string CookieName = "bh-lang";

    public IActionResult OnGet() => Enabled ? LocalRedirect("/") : NotFound();

    public IActionResult OnPost(string lang, string? returnUrl)
    {
        if (!Enabled)
        {
            return NotFound();
        }

        if (lang is not ("de" or "en"))
        {
            return BadRequest();
        }

        // Only the UI language changes; number and date formats stay German.
        string value = CookieRequestCultureProvider.MakeCookieValue(
            new RequestCulture(culture: "de-DE", uiCulture: lang == "en" ? "en" : "de-DE"));
        Response.Cookies.Append(CookieName, value, new CookieOptions
        {
            Path = "/",
            Expires = DateTimeOffset.UtcNow.AddDays(365),
            IsEssential = true,
            SameSite = SameSiteMode.Lax,
            HttpOnly = true,
            Secure = Request.IsHttps,
        });

        return Url.IsLocalUrl(returnUrl) ? LocalRedirect(returnUrl!) : LocalRedirect("/");
    }
}
