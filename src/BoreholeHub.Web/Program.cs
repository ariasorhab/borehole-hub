using BoreholeHub.Web.Data;
using Microsoft.EntityFrameworkCore;

var builder = WebApplication.CreateBuilder(args);

builder.Services.AddRazorPages();

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
            // Unknown provider values are rejected at startup (see open question in return.md).
            throw new InvalidOperationException($"Unbekannter Datenbank-Provider: '{provider}'.");
    }
});

var app = builder.Build();

if (!app.Environment.IsDevelopment())
{
    app.UseExceptionHandler("/Error");
}

app.UseStaticFiles();
app.UseRouting();
app.MapRazorPages();

// No migrations in this demo: create the schema on start if it does not exist.
using (var scope = app.Services.CreateScope())
{
    scope.ServiceProvider.GetRequiredService<BoreholeDbContext>().Database.EnsureCreated();
}

app.Run();
