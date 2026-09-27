namespace LankaMart.Api.Security;

/// Why not just call DateTime.UtcNow everywhere? Because "this refresh token
/// expired eight days ago" is a rule we want to TEST, and a test cannot wait
/// eight days. Injecting the clock lets LankaMart.ServiceTests move time.
///
/// Named IClock rather than ISystemClock on purpose: ASP.NET Core already has
/// an ISystemClock and the collision is confusing.
public interface IClock
{
    DateTime UtcNow { get; }
}

public sealed class SystemClock : IClock
{
    /// SLIDE 19 - UTC, always. Colombo is UTC+05:30; storing local time is how
    /// you get orders that appear to arrive before they were placed.
    public DateTime UtcNow => DateTime.UtcNow;
}
