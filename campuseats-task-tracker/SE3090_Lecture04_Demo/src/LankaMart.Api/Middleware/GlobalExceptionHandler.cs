using LankaMart.Api.Common;
using Microsoft.AspNetCore.Diagnostics;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Npgsql;

namespace LankaMart.Api.Middleware;

// ============================================================================
//  SLIDE 41 - "Error Handling Without Leaking Secrets".
//
//  This one class is the RIGHT column of that slide. It is also the reason no
//  controller in this project contains a single try/catch: errors have exactly
//  two audiences, and both are served here.
//
//     the LOG gets    : exception type, message, stack, SQL state, traceId
//     the CLIENT gets : a Problem Details document (RFC 9457) with a title, a
//                       status and that same traceId - and nothing else
//
//  The traceId is the bridge. A student emails a screenshot saying "it broke";
//  you grep the logs for that id and find the exact stack trace.
//
//  SLIDE 26 closes here too. The PostgresException with SQLSTATE 23505 that we
//  met in Part 3 finally arrives, and becomes a clean 409 Conflict with a
//  human message instead of a 500 that publishes our table and constraint
//  names to an attacker.
// ============================================================================
public class GlobalExceptionHandler : IExceptionHandler
{
    private readonly ILogger<GlobalExceptionHandler> _logger;
    private readonly IHostEnvironment _env;

    public GlobalExceptionHandler(ILogger<GlobalExceptionHandler> logger, IHostEnvironment env)
    {
        _logger = logger;
        _env    = env;
    }

    public async ValueTask<bool> TryHandleAsync(
        HttpContext context, Exception exception, CancellationToken ct)
    {
        var (status, title, detail) = Translate(exception);

        var traceId = context.TraceIdentifier;

        if (status == StatusCodes.Status500InternalServerError)
            _logger.LogError(exception, "Unhandled exception. TraceId {TraceId}", traceId);
        else
            _logger.LogWarning("Handled {ExceptionType}: {Message}. TraceId {TraceId}",
                exception.GetType().Name, exception.Message, traceId);

        var problem = new ProblemDetails
        {
            Status = status,
            Title  = title,

            // The only place environment matters: in Development a developer
            // benefits from the raw message. In production that same string
            // hands out schema details for free, so it is replaced.
            Detail = status == StatusCodes.Status500InternalServerError && !_env.IsDevelopment()
                ? "Please contact support and quote the traceId."
                : detail,

            Type     = $"https://httpstatuses.io/{status}",
            Instance = $"{context.Request.Method} {context.Request.Path}"
        };

        problem.Extensions["traceId"] = traceId;

        context.Response.StatusCode  = status;
        context.Response.ContentType = "application/problem+json";
        await context.Response.WriteAsJsonAsync(problem, ct);

        return true;
    }

    private (int Status, string Title, string Detail) Translate(Exception exception) => exception switch
    {
        // --- Our own domain exceptions (SLIDE 28 for the 401/403 pair) ------
        NotFoundException e =>
            (StatusCodes.Status404NotFound, "Resource not found", e.Message),

        ConflictException e =>
            (StatusCodes.Status409Conflict, "Request conflicts with the current state", e.Message),

        BusinessRuleException e =>
            (StatusCodes.Status400BadRequest, "Business rule violated", e.Message),

        // 401 = we could not establish WHO you are.
        UnauthorizedException e =>
            (StatusCodes.Status401Unauthorized, "Authentication failed", e.Message),

        // 403 = we know exactly who you are, and the answer is still no.
        ForbiddenException e =>
            (StatusCodes.Status403Forbidden, "Access denied", e.Message),

        // --- Database failures, translated                       (SLIDE 26) --
        //  Each of these is a real PostgreSQL SQLSTATE. The DETAIL we return is
        //  written by us and mentions no table, column or constraint name; the
        //  full driver exception is already in the log above.
        DbUpdateException { InnerException: PostgresException pg } => TranslatePostgres(pg),
        PostgresException pg => TranslatePostgres(pg),

        DbUpdateConcurrencyException =>
            (StatusCodes.Status409Conflict, "The record was modified by someone else",
             "Reload the record and try again."),

        // Npgsql cannot reach the server at all - wrong host, database down,
        // firewall. A 503 tells an honest story; a 500 does not.
        NpgsqlException =>
            (StatusCodes.Status503ServiceUnavailable, "The database is unavailable",
             "The service could not reach its database. Please try again shortly."),

        OperationCanceledException =>
            (499, "Client closed the request", "The request was cancelled."),

        _ => (StatusCodes.Status500InternalServerError, "An unexpected error occurred",
              exception.Message)
    };

    /// SLIDE 26 - the SQLSTATE table. These five codes cover almost everything
    /// a well-constrained schema will throw at you in this module.
    private static (int Status, string Title, string Detail) TranslatePostgres(PostgresException pg)
        => pg.SqlState switch
        {
            // 23505 unique_violation - the example from slide 26 and 41.
            "23505" => (StatusCodes.Status409Conflict, "Already exists",
                        "A record with the same unique value already exists."),

            // 23503 foreign_key_violation - e.g. deleting a product that
            // appears on an order, blocked by ON DELETE RESTRICT.
            "23503" => (StatusCodes.Status409Conflict, "Related records exist",
                        "This action conflicts with related records and was refused."),

            // 23514 check_violation - e.g. stock_qty >= 0 or quantity > 0.
            "23514" => (StatusCodes.Status400BadRequest, "Value not allowed",
                        "One of the submitted values breaks a database rule."),

            // 23502 not_null_violation.
            "23502" => (StatusCodes.Status400BadRequest, "Missing required value",
                        "A required field was not supplied."),

            // 40P01 deadlock_detected - PostgreSQL killed one transaction so the
            // other could finish. The correct client behaviour is to RETRY the
            // whole transaction, which is why this is a 409 and not a 500.
            "40P01" => (StatusCodes.Status409Conflict, "Concurrent update, please retry",
                        "Two operations collided. Please retry the request."),

            // 53300 too_many_connections - "sorry, too many clients already".
            // The pool and max_connections disagree (SLIDE 26).
            "53300" => (StatusCodes.Status503ServiceUnavailable, "Database is busy",
                        "The database has no free connections. Please try again shortly."),

            _ => (StatusCodes.Status500InternalServerError, "Database error",
                  "The database rejected this operation.")
        };
}
