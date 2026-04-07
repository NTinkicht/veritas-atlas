namespace VeritasAtlas.Api.Infrastructure.Auth;

public sealed class DevUserStore
{
    private static readonly IReadOnlyList<DevUserRecord> Users = new List<DevUserRecord>
    {
        new("operator1", "password123", "operator"),
        new("reviewer1", "password123", "reviewer"),
        new("publisher1", "password123", "publisher"),
        new("admin1", "password123", "admin")
    };

    public DevUserRecord? Validate(string username, string password)
    {
        return Users.FirstOrDefault(x =>
            string.Equals(x.Username, username, StringComparison.OrdinalIgnoreCase) &&
            x.Password == password);
    }
}

public sealed record DevUserRecord(string Username, string Password, string Role);