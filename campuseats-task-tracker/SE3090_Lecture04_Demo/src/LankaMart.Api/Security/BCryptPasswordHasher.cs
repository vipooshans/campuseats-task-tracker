using LankaMart.Api.Common;
using Microsoft.Extensions.Options;

namespace LankaMart.Api.Security;

// ============================================================================
//  SLIDE 31 step 3 - "Verify password against stored hash (bcrypt)".
//
//  THREE things make this safe, and students routinely miss two of them:
//
//  1. It is SLOW ON PURPOSE. A work factor of 12 means ~4096 internal
//     rounds. SHA-256 hashes billions of candidates per second on a GPU;
//     bcrypt at factor 12 manages a few thousand. That gap is the defence.
//     This is exactly why hashing must NOT be "optimised" for speed.
//
//  2. It SALTS automatically. Look at a stored hash: the salt is embedded
//     in it. Two users with the same password get different hashes, so one
//     cracked password does not unlock every account, and precomputed
//     rainbow tables are useless.
//
//  3. It is ONE WAY. There is no Unhash(). Password reset means issuing a
//     new password, not recovering the old one - which is why any website
//     that emails you your existing password is storing it in plain text.
//
//  Note the fully-qualified BCrypt.Net.BCrypt below. The package's namespace
//  and its class share the name "BCrypt", so a plain `using BCrypt.Net;`
//  makes `BCrypt.HashPassword` ambiguous. Well-known five-minute trap.
// ============================================================================
public sealed class BCryptPasswordHasher : IPasswordHasher
{
    private readonly int _workFactor;

    public BCryptPasswordHasher(IOptions<LankaMartOptions> options)
        => _workFactor = options.Value.BcryptWorkFactor;

    public string Hash(string password)
        => BCrypt.Net.BCrypt.HashPassword(password, _workFactor);

    public bool Verify(string password, string hash)
    {
        try
        {
            return BCrypt.Net.BCrypt.Verify(password, hash);
        }
        catch (Exception)
        {
            // A malformed hash in the database (hand-edited row, failed
            // migration) must read as "wrong password", not as a 500 that
            // tells the caller something interesting about this account.
            return false;
        }
    }
}
