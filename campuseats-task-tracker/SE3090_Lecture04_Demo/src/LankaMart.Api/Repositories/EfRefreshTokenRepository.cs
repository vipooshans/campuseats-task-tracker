using LankaMart.Api.Data;
using LankaMart.Api.Models;
using Microsoft.EntityFrameworkCore;

namespace LankaMart.Api.Repositories;

public sealed class EfRefreshTokenRepository : IRefreshTokenRepository
{
    private readonly AppDbContext _db;
    public EfRefreshTokenRepository(AppDbContext db) => _db = db;

    /// Tracked, because /auth/refresh immediately marks this row revoked as
    /// part of rotation.
    public Task<RefreshToken?> GetByHashAsync(string tokenHash, CancellationToken ct = default)
        => _db.RefreshTokens.FirstOrDefaultAsync(t => t.TokenHash == tokenHash, ct);

    public async Task<RefreshToken> AddAsync(RefreshToken token, CancellationToken ct = default)
    {
        await _db.RefreshTokens.AddAsync(token, ct);
        return token;
    }

    /// SLIDE 32 - "if a refresh token is presented twice, treat it as theft and
    /// revoke the whole family of tokens for that user."
    public async Task<int> RevokeFamilyAsync(
        Guid familyId, DateTime revokedAtUtc, string reason, CancellationToken ct = default)
    {
        var tokens = await _db.RefreshTokens
            .Where(t => t.FamilyId == familyId && t.RevokedAt == null)
            .ToListAsync(ct);

        foreach (var token in tokens)
        {
            token.RevokedAt     = revokedAtUtc;
            token.RevokedReason = reason;
        }

        return tokens.Count;
    }

    /// Used by "log out everywhere" and by an admin disabling an account.
    public async Task<int> RevokeAllForUserAsync(
        long userId, DateTime revokedAtUtc, string reason, CancellationToken ct = default)
    {
        var tokens = await _db.RefreshTokens
            .Where(t => t.UserId == userId && t.RevokedAt == null)
            .ToListAsync(ct);

        foreach (var token in tokens)
        {
            token.RevokedAt     = revokedAtUtc;
            token.RevokedReason = reason;
        }

        return tokens.Count;
    }
}
