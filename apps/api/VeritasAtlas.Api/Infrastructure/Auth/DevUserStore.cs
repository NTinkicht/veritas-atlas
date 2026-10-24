namespace VeritasAtlas.Api.Infrastructure.Auth;

public sealed class DevUserStore
{
    /// <summary>VER-P0-03 (#82): the single synthetic organization tenant of
    /// the Development fixture identities.</summary>
    public static readonly Guid DevelopmentTenantId = Guid.Parse("0f3a6c1e-5b7d-4e2a-9c8f-d1e2f3a4b5c6");

    private static readonly IReadOnlyList<DevUserRecord> Users = new List<DevUserRecord>
    {
        new("operator1", "password123", "operator", DevelopmentTenantId),
        new("reviewer1", "password123", "reviewer", DevelopmentTenantId),
        new("publisher1", "password123", "publisher", DevelopmentTenantId),
        new("admin1", "password123", "admin", DevelopmentTenantId)
    };

    /// <summary>True for any development fixture identity, which must never
    /// be usable outside Development.</summary>
    public static bool IsFixtureUsername(string? username) =>
        username is not null
        && Users.Any(x => string.Equals(
            x.Username, username.Trim(), StringComparison.OrdinalIgnoreCase));

    public DevUserRecord? Validate(string username, string password)
    {
        return Users.FirstOrDefault(x =>
            string.Equals(x.Username, username, StringComparison.OrdinalIgnoreCase) &&
            x.Password == password);
    }
}

/// <param name="TenantId">VER-P0-03 (#82): the configured organization tenant,
/// or null when none is configured (tenant-scoped endpoints then deny).</param>
public sealed record DevUserRecord(string Username, string Password, string Role, Guid? TenantId = null);