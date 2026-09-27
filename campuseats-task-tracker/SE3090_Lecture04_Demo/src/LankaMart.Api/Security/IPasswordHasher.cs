namespace LankaMart.Api.Security;

/// SLIDE 20 + 31 - we store the OUTPUT of a slow hash, never a password.
/// An interface, so the tests can swap in a fast fake: 15 tests x 400 ms of
/// real bcrypt is six seconds of waiting for no extra confidence.
public interface IPasswordHasher
{
    string Hash(string password);

    /// Returns false for a wrong password. Must never throw on bad input -
    /// an exception here would leak information through timing and status codes.
    bool Verify(string password, string hash);
}
