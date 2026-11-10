using BoreholeHub.Web.Data;
using System.Globalization;
using Microsoft.AspNetCore.Localization;
using Microsoft.EntityFrameworkCore;

var builder = WebApplication.CreateBuilder(args);

// UI strings: German is the key, English lives in Resources/*.en.resx (see docs/i18n.md).
builder.Services.AddLocalization(o => o.ResourcesPath = "Resources");
builder.Services.AddRazorPages().AddViewLocalization();

// Provider is "Sqlite" (default) or "SqlServer", set in appsettings.json.
string provider = builder.Configuration["Database:Provider"] ?? "Sqlite";

builder.Services.AddDbContext<BoreholeDbContext>(options =>
{
    switch (provider)
    {
        case "SqlServer":
            options.UseSqlServer(builder.Configuration.GetConnectionString("SqlServer"));
            break;
        case "Sqlite":
            options.UseSqlite(builder.Configuration.GetConnectionString("Sqlite"));
            break;
        default:
            // Unknown provider values are rejected at startup.
            throw new InvalidOperationException($"Unbekannter Datenbank-Provider: '{provider}'.");
    }
});

var app = builder.Build();

if (!app.Environment.IsDevelopment())
{
    app.UseExceptionHandler("/Error");
}

// German number and date formats (decimal comma, dd.MM.yyyy) in both UI languages.
// Only the UI language changes; the language choice is read from the "bh-lang" cookie
// (set by the DE | EN switch on /Sprache). No browser or query-string language detection.
var deDe = new CultureInfo("de-DE");
var en = new CultureInfo("en");
app.UseRequestLocalization(new RequestLocalizationOptions
{
    DefaultRequestCulture = new RequestCulture(culture: "de-DE", uiCulture: "de-DE"),
    SupportedCultures = [deDe],
    SupportedUICultures = [deDe, en],
    RequestCultureProviders = [new CookieRequestCultureProvider { CookieName = "bh-lang" }],
});

// Unknown URLs (and NotFound() results) render Pages/NotFound.cshtml with status 404.
app.UseStatusCodePagesWithReExecute("/NotFound");

app.UseStaticFiles();
app.UseRouting();
app.MapRazorPages();

// No migrations in this demo: create the schema on start if it does not exist.
using (var scope = app.Services.CreateScope())
{
    scope.ServiceProvider.GetRequiredService<BoreholeDbContext>().Database.EnsureCreated();
}

app.Run();

// Makes the entry point visible to WebApplicationFactory in the tests.
public partial class Program;
