namespace VeritasAtlas.Application.Interfaces;

/// <summary>
/// VER-P0-02 (#79): server-side record of revoked access tokens, keyed by the
/// token's <c>jti</c>. Implementations store only a one-way verifier of the
/// <c>jti</c>, never the bearer itself.
/// </summary>
public interface IAccessTokenRevocationStore
{
    /// <summary>
    /// Revokes the token identified by <paramref name="jti"/> until
    /// <paramref name="expiresAtUtc"/>. Idempotent: revoking an already revoked
    /// token succeeds without error.
    /// </summary>
    Task RevokeAsync(string jti, DateTime expiresAtUtc, CancellationToken cancellationToken);

    /// <summary>
    /// True when the token identified by <paramref name="jti"/> was revoked.
    /// Throws when the store cannot be read; callers must fail closed.
    /// </summary>
    Task<bool> IsRevokedAsync(string jti, CancellationToken cancellationToken);
}
