using System.Collections.Concurrent;
using System.Globalization;
using System.Threading.RateLimiting;

namespace VeritasAtlas.Api.Infrastructure.Auth;

/// <summary>
/// Login brute-force controls (VER-P0-02). The request limiter is partitioned
/// per remote client so one client cannot exhaust the login budget of all
/// others; <see cref="LoginAttemptGuard"/> bounds password guessing against a
/// single account across clients.
/// </summary>
public static class LoginRateLimiting
{
    public const string PolicyName = "auth-login";
    public const int PermitLimit = 6;
    public static readonly TimeSpan Window = TimeSpan.FromMinutes(1);

    public static string PartitionKey(HttpContext context) =>
        context.Connection.RemoteIpAddress?.ToString() ?? "unknown";

    public static RateLimitPartition<string> Partition(HttpContext context) =>
        RateLimitPartition.GetFixedWindowLimiter(
            PartitionKey(context),
            _ => new FixedWindowRateLimiterOptions
            {
                PermitLimit = PermitLimit,
                Window = Window,
                QueueLimit = 0,
                AutoReplenishment = true
            });
}

/// <summary>
/// In-memory per-account failed-login lockout. Usernames are normalized and
/// unknown usernames are tracked exactly like real ones, so the guard is not
/// an account-enumeration oracle.
/// </summary>
public sealed class LoginAttemptGuard
{
    public const int MaxFailures = 5;
    public const int MaxCredentialLength = 256;
    public static readonly TimeSpan FailureWindow = TimeSpan.FromMinutes(15);

    private readonly TimeProvider _time;
    private readonly ConcurrentDictionary<string, FailureRecord> _failures = new();

    public LoginAttemptGuard(TimeProvider time)
    {
        _time = time;
    }

    public static string Normalize(string username) =>
        username.Trim().ToLower(CultureInfo.InvariantCulture);

    public static bool IsWellFormed(string? username, string? password) =>
        !string.IsNullOrWhiteSpace(username)
        && !string.IsNullOrEmpty(password)
        && username.Length <= MaxCredentialLength
        && password.Length <= MaxCredentialLength;

    public bool IsLockedOut(string username)
    {
        var key = Normalize(username);
        if (!_failures.TryGetValue(key, out var record)) return false;
        if (_time.GetUtcNow() - record.WindowStart >= FailureWindow)
        {
            _failures.TryRemove(new KeyValuePair<string, FailureRecord>(key, record));
            return false;
        }
        return record.Count >= MaxFailures;
    }

    public void RecordFailure(string username)
    {
        var now = _time.GetUtcNow();
        _failures.AddOrUpdate(
            Normalize(username),
            _ => new FailureRecord(now, 1),
            (_, existing) => now - existing.WindowStart >= FailureWindow
                ? new FailureRecord(now, 1)
                : existing with { Count = existing.Count + 1 });
    }

    public void RecordSuccess(string username) =>
        _failures.TryRemove(Normalize(username), out _);

    private sealed record FailureRecord(DateTimeOffset WindowStart, int Count);
}
