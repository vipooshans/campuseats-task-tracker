using LankaMart.Api.Common;
using LankaMart.Api.Security;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;

namespace LankaMart.Api.Data;

// ============================================================================
//  The twenty seconds of startup work that decides whether a lab of thirty
//  students is debugging PostgreSQL or learning authorization.
//
//  Every failure below prints WHAT went wrong and WHICH COMMAND fixes it.
//  A stack trace saying "relation \"users\" does not exist" is technically
//  accurate and pedagogically useless.
//
//  SLIDE 19 - "Schema lives in versioned migrations. Never edit production
//  tables by hand." Note that automatic migration on startup is convenient
//  here and OFF outside Development: in production, applying a schema change
//  is a deliberate, reviewable deployment step.
// ============================================================================
public static class DatabaseStartup
{
    public static async Task PrepareAsync(WebApplication app)
    {
        using var scope = app.Services.CreateScope();

        var services = scope.ServiceProvider;
        var logger   = services.GetRequiredService<ILogger<Program>>();
        var db       = services.GetRequiredService<AppDbContext>();
        var options  = services.GetRequiredService<IOptions<LankaMartOptions>>().Value;

        // --- 1. Can we reach PostgreSQL at all? ---------------------------
        bool canConnect;
        try
        {
            canConnect = await db.Database.CanConnectAsync();
        }
        catch (Exception ex)
        {
            canConnect = false;
            logger.LogError("Could not reach PostgreSQL: {Message}", ex.Message);
        }

        if (!canConnect)
        {
            logger.LogError(
                "\n" +
                "  ============================================================\n" +
                "   DATABASE NOT REACHABLE\n" +
                "  ============================================================\n" +
                "   Check, in this order:\n" +
                "     1. Is PostgreSQL running?\n" +
                "          Windows : services.msc -> postgresql-x64-16\n" +
                "          macOS   : brew services list\n" +
                "          Linux   : sudo systemctl status postgresql\n" +
                "          Docker  : docker compose -f db/docker-compose.yml up -d\n" +
                "     2. Is the connection string set? It must NOT be in\n" +
                "        appsettings.json. Use one of:\n" +
                "          dotnet user-secrets set \"ConnectionStrings:LankaMartDb\" \"...\"\n" +
                "          export ConnectionStrings__LankaMartDb=\"...\"\n" +
                "     3. Does the database exist? This creates it:\n" +
                "          dotnet ef database update\n" +
                "  ============================================================");
            return;
        }

        // --- 2. Migrations ------------------------------------------------
        var defined = db.Database.GetMigrations().ToList();

        if (defined.Count == 0)
        {
            logger.LogError(
                "\n" +
                "  ============================================================\n" +
                "   NO MIGRATIONS IN THIS PROJECT YET\n" +
                "  ============================================================\n" +
                "   This is expected on a fresh clone - the Migrations folder is\n" +
                "   deliberately not shipped, because generating it is a lab task\n" +
                "   (SLIDE 19). Run these two commands from src/LankaMart.Api:\n" +
                "\n" +
                "       dotnet ef migrations add InitialCreate\n" +
                "       dotnet ef database update\n" +
                "\n" +
                "   Then open the generated file and read it: that C# is the DDL\n" +
                "   on slide 20, produced from AppDbContext.\n" +
                "  ============================================================");
            return;
        }

        var pending = (await db.Database.GetPendingMigrationsAsync()).ToList();

        if (pending.Count > 0)
        {
            if (options.ApplyMigrationsOnStartup)
            {
                logger.LogWarning("Applying {Count} pending migration(s): {Names}",
                    pending.Count, string.Join(", ", pending));

                await db.Database.MigrateAsync();

                logger.LogInformation("Migrations applied.");
            }
            else
            {
                logger.LogError(
                    "The database is {Count} migration(s) behind ({Names}). " +
                    "Run:  dotnet ef database update",
                    pending.Count, string.Join(", ", pending));
                return;
            }
        }

        // --- 3. Seed ------------------------------------------------------
        if (!options.SeedDemoData)
        {
            logger.LogInformation("Seeding is disabled (LankaMart:SeedDemoData = false).");
            return;
        }

        if (!app.Environment.IsDevelopment())
        {
            // A seeder that plants a known password in production is a back
            // door with good intentions. It refuses, regardless of the flag.
            logger.LogWarning(
                "Seeding requested but the environment is {Env}. Refusing: the demo " +
                "accounts share a published password.", app.Environment.EnvironmentName);
            return;
        }

        var hasher = services.GetRequiredService<IPasswordHasher>();
        await DatabaseSeeder.SeedAsync(db, hasher, options, logger);
    }
}
