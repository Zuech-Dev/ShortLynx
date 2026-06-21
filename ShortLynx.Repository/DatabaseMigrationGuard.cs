using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using ShortLynx.Data.Context;

namespace ShortLynx.Repository;

/// <summary>
/// Startup guard that fails fast when the database is behind the EF migrations. Intended to be called
/// in Development so schema drift (a generated-but-unapplied migration) surfaces at boot with a clear
/// message, instead of as a cryptic query-time error such as "column does not exist".
/// </summary>
public static class DatabaseMigrationGuard
{
    /// <summary>
    /// Throws <see cref="InvalidOperationException"/> if the <see cref="ShortLynxDbContext"/> has
    /// unapplied migrations. Resolve by running <c>dotnet ef database update</c>.
    /// </summary>
    /// <remarks>
    /// If the configured provider has no migrations assembly to enumerate (e.g. the SQLite test/dev
    /// path, which builds its schema with EnsureCreated rather than migrations), the check is skipped
    /// rather than throwing — the guard is a safety net and must never be the thing that breaks startup.
    /// </remarks>
    public static void ThrowIfPending(IServiceProvider services)
    {
        using var scope = services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<ShortLynxDbContext>();

        List<string> pending;
        try
        {
            pending = db.Database.GetPendingMigrations().ToList();
        }
        catch (Exception ex)
        {
            // Couldn't determine migration state (no migrations assembly, DB unreachable, etc.).
            // Don't block startup on the guard itself; the real connection will surface any DB problem.
            Console.Error.WriteLine($"[DatabaseMigrationGuard] skipped — could not read migration state: {ex.Message}");
            return;
        }

        if (pending.Count == 0)
            return;

        throw new InvalidOperationException(
            $"Database is behind the migrations — {pending.Count} unapplied: {string.Join(", ", pending)}. " +
            "Run `dotnet ef database update` before starting the app.");
    }
}
