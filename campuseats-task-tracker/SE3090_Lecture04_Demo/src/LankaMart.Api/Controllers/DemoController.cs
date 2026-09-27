using System.IdentityModel.Tokens.Jwt;
using LankaMart.Api.Common;
using LankaMart.Api.Data;
using LankaMart.Api.Models;
using LankaMart.Api.Security;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace LankaMart.Api.Controllers;

// ############################################################################
// #                                                                          #
// #   CLASSROOM ENDPOINTS. SOME OF THIS CODE IS DELIBERATELY WRONG.          #
// #                                                                          #
// #   Guarded by [TeachingEndpoint]: 404 unless Development AND              #
// #   LankaMart:EnableTeachingEndpoints = true. Never enable in production.   #
// #                                                                          #
// #   Each action exists to make one slide FAIL on the projector, which       #
// #   teaches faster than any diagram. Watch the terminal while you call     #
// #   them: SQL logging is on in Development, so the statements PostgreSQL    #
// #   actually ran scroll past in real time.                                  #
// #                                                                          #
// ############################################################################
[ApiController]
[Route("api/v1/demo")]
[Produces("application/json")]
[AllowAnonymous]
[TeachingEndpoint]
public class DemoController : ControllerBase
{
    private readonly AppDbContext         _db;
    private readonly IPasswordHasher      _hasher;
    private readonly ILogger<DemoController> _logger;

    public DemoController(AppDbContext db, IPasswordHasher hasher, ILogger<DemoController> logger)
    {
        _db     = db;
        _hasher = hasher;
        _logger = logger;
    }

    // ========================================================================
    //  SLIDE 40 - SQL INJECTION, live.
    // ========================================================================
    /// <summary>Search products with concatenated SQL (vulnerable) or parameters (safe).</summary>
    /// <remarks>
    /// THE DEMO:
    ///   1. /api/v1/demo/injection?name=Mouse&amp;mode=vulnerable   -> 1 row
    ///   2. /api/v1/demo/injection?name=%27 OR %271%27=%271&amp;mode=vulnerable
    ///        (that is:  ' OR '1'='1  )                             -> EVERY row
    ///   3. the same input with mode=safe                            -> 0 rows
    ///
    /// Step 2 is authentication bypass, data theft and, with a slightly longer
    /// payload, data destruction - from a text box. Step 3 is the same input
    /// treated as DATA rather than SYNTAX, which is the entire lesson.
    /// The response echoes the SQL that was built so the class can see why.
    /// </remarks>
    [HttpGet("injection")]
    public async Task<IActionResult> Injection(
        [FromQuery] string name = "Mouse",
        [FromQuery] string mode = "safe",
        CancellationToken ct = default)
    {
        if (mode.Equals("vulnerable", StringComparison.OrdinalIgnoreCase))
        {
            // ################################################################
            //  NEVER WRITE THIS. Untrusted input concatenated into SQL text.
            //  The database receives one string and cannot tell which parts
            //  the developer wrote and which parts the attacker wrote.
            // ################################################################
            var sql = "SELECT * FROM products WHERE name ILIKE '%" + name + "%'";

            _logger.LogWarning("Running DELIBERATELY VULNERABLE SQL: {Sql}", sql);

            var rows = await _db.Products.FromSqlRaw(sql).AsNoTracking().ToListAsync(ct);

            return Ok(new
            {
                mode          = "vulnerable",
                sqlSentToPostgres = sql,
                rowsReturned  = rows.Count,
                products      = rows.Select(p => new { p.Id, p.Name, p.Price }),
                lesson        = "The input became part of the query's SYNTAX. "
                              + "Try name=' OR '1'='1 and count the rows."
            });
        }

        // ------------------------------------------------------------------
        //  SAFE. Two equally correct forms.
        // ------------------------------------------------------------------
        var pattern = $"%{name}%";

        // (a) parameterized raw SQL - note the {} placeholder becomes $1
        var viaSql = await _db.Products
            .FromSqlInterpolated($"SELECT * FROM products WHERE name ILIKE {pattern}")
            .AsNoTracking().ToListAsync(ct);

        // (b) LINQ - parameterized by EF Core, no SQL written by hand at all
        var viaLinq = await _db.Products
            .Where(p => EF.Functions.ILike(p.Name, pattern))
            .AsNoTracking().ToListAsync(ct);

        return Ok(new
        {
            mode = "safe",
            sqlSentToPostgres = "SELECT * FROM products WHERE name ILIKE $1   (value bound separately)",
            rowsReturned_parameterizedSql = viaSql.Count,
            rowsReturned_linq             = viaLinq.Count,
            products = viaLinq.Select(p => new { p.Id, p.Name, p.Price }),
            lesson   = "The value travelled SEPARATELY from the SQL text, so it can "
                     + "never be parsed as syntax. Escaping is not a substitute."
        });
    }

    // ========================================================================
    //  SLIDE 25 - THE N+1 PROBLEM.
    // ========================================================================
    /// <summary>Loads orders and touches each customer WITHOUT Include. Watch the log.</summary>
    /// <remarks>
    /// Count the SELECT statements in the terminal: one for the orders, then one
    /// more per order. Ten orders, eleven queries. On a page of 100 rows that is
    /// 101 network round trips, and the reason somebody will tell you
    /// "PostgreSQL is slow" when the schema is fine.
    /// </remarks>
    [HttpGet("n-plus-one")]
    public async Task<IActionResult> NPlusOne(CancellationToken ct)
    {
        _logger.LogWarning("--- N+1 DEMO: expect 1 query for orders, then one per order ---");

        var orders = await _db.Orders.AsNoTracking().Take(10).ToListAsync(ct);

        var names = new List<string>();
        foreach (var order in orders)
        {
            // Lazy loading is off in this project, so this is null rather than a
            // hidden query - we make the extra query explicit instead, which is
            // exactly what lazy loading would have done behind your back.
            var customer = await _db.Users.AsNoTracking()
                .FirstOrDefaultAsync(u => u.Id == order.CustomerId, ct);

            names.Add(customer?.Name ?? "?");
        }

        return Ok(new
        {
            pattern      = "N + 1",
            ordersLoaded = orders.Count,
            queriesRun   = 1 + orders.Count,
            customers    = names,
            fix          = "GET /api/v1/demo/with-include"
        });
    }

    /// <summary>The same data in ONE query, using Include.</summary>
    [HttpGet("with-include")]
    public async Task<IActionResult> WithInclude(CancellationToken ct)
    {
        _logger.LogWarning("--- INCLUDE DEMO: expect exactly ONE query with a JOIN ---");

        var orders = await _db.Orders
            .Include(o => o.Customer)
            .AsNoTracking().Take(10).ToListAsync(ct);

        return Ok(new
        {
            pattern      = "single query with JOIN",
            ordersLoaded = orders.Count,
            queriesRun   = 1,
            customers    = orders.Select(o => o.Customer?.Name ?? "?")
        });
    }

    // ========================================================================
    //  SLIDE 26 + 41 - a real PostgreSQL error, translated.
    // ========================================================================
    /// <summary>Inserts a duplicate category name to trigger SQLSTATE 23505.</summary>
    /// <remarks>
    /// Expect 409 Conflict with a clean Problem Details body. Now look at the
    /// terminal: the full Npgsql exception, the constraint name, the whole stack.
    /// Two audiences, one error - slide 41.
    /// </remarks>
    [HttpGet("unique-violation")]
    public async Task<IActionResult> UniqueViolation(CancellationToken ct)
    {
        var existing = await _db.Categories.AsNoTracking().FirstOrDefaultAsync(ct);
        if (existing is null) return Ok(new { note = "Seed the database first." });

        // The UNIQUE index on categories.name will reject this.
        _db.Categories.Add(new Category { Name = existing.Name });

        await _db.SaveChangesAsync(ct);     // throws -> handler maps 23505 -> 409

        return Ok(new { note = "unreachable" });
    }

    /// <summary>Throws an ordinary exception, to show what the client gets.</summary>
    /// <remarks>Expect 500 with a traceId and no stack trace. Compare with /leaky-error.</remarks>
    [HttpGet("boom")]
    public IActionResult Boom()
        => throw new InvalidOperationException(
               "Simulated failure deep inside a service, with a database password in it: hunter2");

    /// <summary>What a careless API would have returned. NOT a real response.</summary>
    /// <remarks>
    /// SLIDE 41, left column. This is a hard-coded STRING, not a real error -
    /// nothing here is leaking. Show it beside /boom and ask the class what an
    /// attacker learns from it: table and constraint names, file paths, the
    /// project layout, the ORM and driver in use. Every item shortens an attack.
    /// </remarks>
    [HttpGet("leaky-error")]
    public IActionResult LeakyError() => Ok(new
    {
        warning = "SIMULATION - this is what NOT to return.",
        whatABadApiReturns = new[]
        {
            "HTTP 500",
            "Npgsql.PostgresException (0x80004005):",
            "23505: duplicate key value violates unique constraint \"ux_users_email\"",
            "   at LankaMart.Api.Repositories.EfUserRepository.AddAsync()",
            "   in C:\\src\\SE3090\\src\\LankaMart.Api\\Repositories\\EfUserRepository.cs:line 61",
            "   Npgsql 8.0.10 / EF Core 8.0.10 / .NET 8.0.10"
        },
        whatAnAttackerLearns = new[]
        {
            "the table and constraint names (schema reconnaissance)",
            "the file system layout and project structure",
            "the ORM and driver versions, so they can look up known CVEs",
            "which internal method failed, and roughly why"
        },
        compareWith = "GET /api/v1/demo/boom"
    });

    // ========================================================================
    //  SLIDE 30 - "a JWT hides nothing".
    // ========================================================================
    /// <summary>Decodes a JWT WITHOUT verifying it, to prove it is not encrypted.</summary>
    /// <remarks>
    /// Paste your own accessToken. You will get the header and payload back as
    /// plain JSON, with no key and no password. The signature is what stops you
    /// CHANGING it - not what stops you READING it. Hence: no passwords, no NIC
    /// numbers, no salaries in a payload.
    /// </remarks>
    [HttpGet("token-inspect")]
    public IActionResult InspectToken([FromQuery] string token)
    {
        if (string.IsNullOrWhiteSpace(token))
            return BadRequest(new { error = "Pass ?token=<your access token>" });

        try
        {
            // ReadJwtToken does NOT validate the signature. That is the point.
            var jwt = new JwtSecurityTokenHandler().ReadJwtToken(token);

            return Ok(new
            {
                header    = jwt.Header.ToDictionary(h => h.Key, h => h.Value?.ToString()),
                payload   = jwt.Claims.Select(c => new { type = c.Type, value = c.Value }),
                expiresAt = jwt.ValidTo,
                signaturePresent = !string.IsNullOrEmpty(jwt.RawSignature),
                lesson = "Decoded with no key at all. A JWT is SIGNED, not ENCRYPTED: "
                       + "anyone can read it, only the key holder can forge one."
            });
        }
        catch (Exception ex)
        {
            return BadRequest(new { error = "Not a readable JWT.", detail = ex.Message });
        }
    }

    // ========================================================================
    //  SLIDE 31 - why bcrypt, in one request.
    // ========================================================================
    /// <summary>Hashes the same password twice and verifies both.</summary>
    /// <remarks>
    /// The two hashes are DIFFERENT, because bcrypt salts automatically - and
    /// both verify against the same password. That is why one cracked password
    /// does not unlock every account, and why rainbow tables are useless here.
    /// Note the elapsed milliseconds: slowness is the security feature.
    /// </remarks>
    [HttpGet("hash-password")]
    public IActionResult HashPassword([FromQuery] string password = "Password123!")
    {
        var started = DateTime.UtcNow;
        var hashA   = _hasher.Hash(password);
        var hashB   = _hasher.Hash(password);
        var elapsed = (DateTime.UtcNow - started).TotalMilliseconds;

        return Ok(new
        {
            password,
            hashA,
            hashB,
            identical      = hashA == hashB,
            bothVerify     = _hasher.Verify(password, hashA) && _hasher.Verify(password, hashB),
            wrongPassword  = _hasher.Verify(password + "x", hashA),
            millisecondsForTwoHashes = Math.Round(elapsed),
            lesson = "Different hashes for the same password (automatic salting), "
                   + "both valid, and deliberately slow. There is no Unhash()."
        });
    }

    // ========================================================================
    //  SLIDE 23 - who are we connected as?
    // ========================================================================
    /// <summary>Reports the PostgreSQL version, database and login role in use.</summary>
    /// <remarks>
    /// If current_user says "postgres", the API is connected as the superuser and
    /// a single injection flaw becomes a DROP DATABASE. Slide 23's least-
    /// privilege rule, made checkable.
    /// </remarks>
    [HttpGet("db-info")]
    public async Task<IActionResult> DbInfo(CancellationToken ct)
    {
        await _db.Database.OpenConnectionAsync(ct);
        try
        {
            var connection = _db.Database.GetDbConnection();
            using var command = connection.CreateCommand();
            command.CommandText =
                "SELECT version() || ' | db=' || current_database() || ' | user=' || current_user";

            var info = (await command.ExecuteScalarAsync(ct))?.ToString() ?? "";

            return Ok(new
            {
                connection = info,
                note = "current_user should be a least-privilege application role, "
                     + "never 'postgres'. Pooling: closing a connection returns it "
                     + "to the pool rather than tearing down the TCP session."
            });
        }
        finally
        {
            await _db.Database.CloseConnectionAsync();
        }
    }
}
