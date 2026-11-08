using BoreholeHub.Web.Data;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.DependencyInjection;

namespace BoreholeHub.Tests;

/// <summary>Starts the web app against a temporary SQLite file that is deleted on dispose.</summary>
public class WebAppFactory : WebApplicationFactory<Program>
{
    private readonly string _dbFile = Path.Combine(Path.GetTempPath(), $"boreholehub-test-{Guid.NewGuid():N}.db");

    protected override void ConfigureWebHost(IWebHostBuilder builder)
    {
        builder.UseSetting("Database:Provider", "Sqlite");
        builder.UseSetting("ConnectionStrings:Sqlite", $"Data Source={_dbFile};Pooling=False");
    }

    /// <summary>Runs <paramref name="action"/> with a database context of the running app.</summary>
    public async Task WithDbAsync(Func<BoreholeDbContext, Task> action)
    {
        using var scope = Services.CreateScope();
        await action(scope.ServiceProvider.GetRequiredService<BoreholeDbContext>());
    }

    protected override void Dispose(bool disposing)
    {
        base.Dispose(disposing);
        foreach (var file in new[] { _dbFile, _dbFile + "-wal", _dbFile + "-shm", _dbFile + "-journal" })
        {
            try { File.Delete(file); } catch (IOException) { /* best effort */ }
        }
    }
}
