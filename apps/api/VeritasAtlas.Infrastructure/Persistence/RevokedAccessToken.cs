namespace VeritasAtlas.Infrastructure.Persistence;

/// <summary>
/// A revoked access token (VER-P0-02, #79). <see cref="JtiSha256"/> is the
/// lowercase hex SHA-256 of the token's <c>jti</c>; the bearer is never stored.
/// </summary>
public sealed class RevokedAccessToken
{
    public string JtiSha256 { get; set; } = string.Empty;

    public DateTime ExpiresAtUtc { get; set; }

    public DateTime RevokedAtUtc { get; set; }
}
