using System.Security.Cryptography;
using System.Text;
using Microsoft.EntityFrameworkCore;
using VeritasAtlas.Application.Interfaces;
using VeritasAtlas.Infrastructure.Persistence;

namespace VeritasAtlas.Infrastructure.Services;

/// <summary>
/// PostgreSQL-backed revocation list (VER-P0-02, #79). Revocation is a single
/// <c>INSERT ... ON CONFLICT DO NOTHING</c>, so concurrent or repeated logouts
/// of the same token are idempotent.
/// </summary>
public sealed class EfAccessTokenRevocationStore : IAccessTokenRevocationStore
{
    private readonly VeritasAtlasDbContext _db;
    private readonly TimeProvider _timeProvider;

    public EfAccessTokenRevocationStore(VeritasAtlasDbContext db, TimeProvider timeProvider)
    {
        _db = db;
        _timeProvider = timeProvider;
    }

    public async Task RevokeAsync(string jti, DateTime expiresAtUtc, CancellationToken cancellationToken)
    {
        var verifier = Verifier(jti);
        var revokedAtUtc = _timeProvider.GetUtcNow().UtcDateTime;
        var expires = DateTime.SpecifyKind(expiresAtUtc, DateTimeKind.Utc);
        await _db.Database.ExecuteSqlInterpolatedAsync(
            $"""
            INSERT INTO revoked_access_tokens (jti_sha256, expires_at_utc, revoked_at_utc)
            VALUES ({verifier}, {expires}, {revokedAtUtc})
            ON CONFLICT (jti_sha256) DO NOTHING
            """,
            cancellationToken);
    }

    public Task<bool> IsRevokedAsync(string jti, CancellationToken cancellationToken)
    {
        var verifier = Verifier(jti);
        return _db.RevokedAccessTokens
            .AsNoTracking()
            .AnyAsync(token => token.JtiSha256 == verifier, cancellationToken);
    }

    public static string Verifier(string jti)
    {
        if (string.IsNullOrWhiteSpace(jti))
        {
            throw new ArgumentException("A token identifier is required.", nameof(jti));
        }

        return Convert.ToHexStringLower(SHA256.HashData(Encoding.UTF8.GetBytes(jti)));
    }
}
