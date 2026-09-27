namespace LankaMart.Api.Common;

// ============================================================
//  SLIDE 41 - errors handled in ONE place.
//
//  Services throw these. They know nothing about HTTP - no status
//  codes, no ActionResult, no HttpContext - which is exactly why
//  they can be unit-tested without a web server.
//  GlobalExceptionHandler translates each type into the right code.
//
//  Two types are NEW this lecture, and the pair is the single most
//  confused distinction in the module (SLIDE 28):
//
//      UnauthorizedException -> 401  "we do not know who you are"
//      ForbiddenException    -> 403  "we know exactly who you are,
//                                     and the answer is still no"
// ============================================================

/// Base type for errors that are the CALLER's fault, not a server bug.
public abstract class DomainException : Exception
{
    protected DomainException(string message) : base(message) { }
}

/// -> 404 Not Found.
public sealed class NotFoundException : DomainException
{
    public NotFoundException(string resource, object id)
        : base($"{resource} with id {id} was not found.") { }
}

/// -> 409 Conflict. Well-formed input that conflicts with current state:
/// duplicate email, insufficient stock, cancelling a delivered order.
public sealed class ConflictException : DomainException
{
    public ConflictException(string message) : base(message) { }
}

/// -> 400 Bad Request. A BUSINESS rule was broken. Structural validation
/// ([Required], [Range]) is handled earlier by [ApiController] and never
/// reaches here.
public sealed class BusinessRuleException : DomainException
{
    public BusinessRuleException(string message) : base(message) { }
}

/// -> 401 Unauthorized, which HTTP misnames: it means UNAUTHENTICATED.
/// Wrong password, unknown email, expired or reused refresh token.
///
/// SLIDE 31 (speaker notes) - the message is deliberately identical for
/// "no such user" and "wrong password". Different messages let an attacker
/// enumerate which email addresses are registered.
public sealed class UnauthorizedException : DomainException
{
    public UnauthorizedException(string message = "Invalid email or password.")
        : base(message) { }
}

/// -> 403 Forbidden. Authenticated, identified, and still not permitted.
/// This is what an object-level authorization failure throws when customer A
/// asks for customer B's order (SLIDE 36 - OWASP API #1).
public sealed class ForbiddenException : DomainException
{
    public ForbiddenException(string message = "You do not have permission to perform this action.")
        : base(message) { }
}
