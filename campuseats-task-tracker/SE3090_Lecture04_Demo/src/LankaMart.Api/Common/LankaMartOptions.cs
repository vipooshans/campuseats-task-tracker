using System.ComponentModel.DataAnnotations;

namespace LankaMart.Api.Common;

// ============================================================
//  SLIDE 23 - strongly typed configuration.
//  Code never changes between environments; only the VALUES do.
// ============================================================
public class LankaMartOptions
{
    public const string SectionName = "LankaMart";

    // --- Paging (Lecture 03, slide 43) ----------------------------------
    [Range(1, 500)] public int MaxPageSize     { get; set; } = 100;
    [Range(1, 500)] public int DefaultPageSize { get; set; } = 20;

    // --- Pricing rules (Lecture 03, slide 52) ---------------------------
    public decimal FreeDeliveryThreshold   { get; set; } = 10_000m;
    public decimal DeliveryFee             { get; set; } = 350m;
    public decimal CashOnDeliverySurcharge { get; set; } = 250m;

    // --- Security / integration (SLIDE 39) ------------------------------
    /// CORS allow-list. Never "*" together with credentials.
    public string[] AllowedOrigins { get; set; } = Array.Empty<string>();

    /// SLIDE 31 - bcrypt is deliberately slow to resist brute force.
    /// 12 means 2^12 = 4096 internal rounds, roughly 200-400 ms per hash on a
    /// laptop. Raise it as hardware improves; never lower it below 10.
    [Range(10, 16)] public int BcryptWorkFactor { get; set; } = 12;

    // --- Startup behaviour ----------------------------------------------
    /// Applies pending EF Core migrations on startup. Convenient in a lecture,
    /// deliberately false outside Development: production schema changes belong
    /// in a deployment step you can review and roll back (SLIDE 19).
    public bool ApplyMigrationsOnStartup { get; set; }

    /// Inserts the demo roles, users and catalogue when the database is empty.
    public bool SeedDemoData { get; set; }

    /// The password given to every seeded demo account. Development only -
    /// the seeder refuses to run outside Development.
    public string DemoUserPassword { get; set; } = "Password123!";

    /// Teaching only: exposes /api/v1/demo/... The endpoints there include
    /// deliberately wrong code (SQL injection, N+1, leaked stack traces) whose
    /// whole purpose is to be shown failing on a projector.
    public bool EnableTeachingEndpoints { get; set; }
}
