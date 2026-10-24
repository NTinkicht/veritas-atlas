using System.Collections.Concurrent;
using VeritasAtlas.Application.Interfaces;

namespace VeritasAtlas.Api.Tests;

/// <summary>
/// Test double for the PostgreSQL revocation store (VER-P0-02, #79), used by
/// HTTP pipeline tests that run without a database.
/// </summary>
internal sealed class InMemoryAccessTokenRevocationStore : IAccessTokenRevocationStore
{
    public ConcurrentDictionary<string, DateTime> Revoked { get; } = new(StringComparer.Ordinal);

    public Task RevokeAsync(string jti, DateTime expiresAtUtc, CancellationToken cancellationToken)
    {
        Revoked.TryAdd(jti, expiresAtUtc);
        return Task.CompletedTask;
    }

    public Task<bool> IsRevokedAsync(string jti, CancellationToken cancellationToken) =>
        Task.FromResult(Revoked.ContainsKey(jti));
}
