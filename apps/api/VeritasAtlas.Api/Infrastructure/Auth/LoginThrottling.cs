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
/// <para>
/// Each credential check first reserves an attempt atomically
/// (<see cref="TryReserveAttempt"/>), so concurrent requests for one account
/// cannot all pass the lockout check before any failure is recorded. A
/// successful login releases the account. Expired records are swept and the
/// table is capped, so rotating unknown usernames cannot grow memory without
/// bound.
/// </para>
/// </summary>
public sealed class LoginAttemptGuard
{
    public const int MaxFailures = 5;
    public const int MaxCredentialLength = 256;
    public const int DefaultMaxTrackedAccounts = 50_000;
    public static readonly TimeSpan FailureWindow = TimeSpan.FromMinutes(15);

    private readonly TimeProvider _time;
    private readonly int _maxTrackedAccounts;
    private readonly object _gate = new();
    private readonly Dictionary<string, AttemptRecord> _attempts = new(StringComparer.Ordinal);

    public LoginAttemptGuard(TimeProvider time)
        : this(time, DefaultMaxTrackedAccounts)
    {
    }

    public LoginAttemptGuard(TimeProvider time, int maxTrackedAccounts)
    {
        ArgumentOutOfRangeException.ThrowIfLessThan(maxTrackedAccounts, 1);
        _time = time;
        _maxTrackedAccounts = maxTrackedAccounts;
    }

    public static string Normalize(string username) =>
        username.Trim().ToLower(CultureInfo.InvariantCulture);

    public static bool IsWellFormed(string? username, string? password) =>
        !string.IsNullOrWhiteSpace(username)
        && !string.IsNullOrEmpty(password)
        && username.Length <= MaxCredentialLength
        && password.Length <= MaxCredentialLength;

    /// <summary>Number of accounts currently tracked (for bounds tests).</summary>
    public int TrackedAccountCount
    {
        get { lock (_gate) return _attempts.Count; }
    }

    /// <summary>
    /// Atomically reserves one credential check for <paramref name="username"/>.
    /// Returns false (locked out) when <see cref="MaxFailures"/> attempts were
    /// already made in the current window. A reserved attempt counts as a
    /// failure unless <see cref="RecordSuccess"/> releases the account.
    /// </summary>
    public bool TryReserveAttempt(string username)
    {
        var key = Normalize(username);
        var now = _time.GetUtcNow();
        lock (_gate)
        {
            if (_attempts.TryGetValue(key, out var record) && !IsExpired(record, now))
            {
                if (record.Count >= MaxFailures) return false;
                _attempts[key] = record with { Count = record.Count + 1 };
                return true;
            }

            if (record is null && _attempts.Count >= _maxTrackedAccounts)
            {
                MakeRoom(now);
            }
            _attempts[key] = new AttemptRecord(now, 1);
            return true;
        }
    }

    public bool IsLockedOut(string username)
    {
        var now = _time.GetUtcNow();
        lock (_gate)
        {
            return _attempts.TryGetValue(Normalize(username), out var record)
                && !IsExpired(record, now)
                && record.Count >= MaxFailures;
        }
    }

    public void RecordSuccess(string username)
    {
        lock (_gate) _attempts.Remove(Normalize(username));
    }

    private static bool IsExpired(AttemptRecord record, DateTimeOffset now) =>
        now - record.WindowStart >= FailureWindow;

    // Called under _gate when the table is full: drop expired records first;
    // if every record is still live, evict the one with the oldest window
    // (closest to expiring anyway) so memory stays bounded.
    private void MakeRoom(DateTimeOffset now)
    {
        var expired = _attempts.Where(x => IsExpired(x.Value, now)).Select(x => x.Key).ToList();
        foreach (var key in expired) _attempts.Remove(key);
        if (_attempts.Count < _maxTrackedAccounts) return;
        var oldest = _attempts.MinBy(x => x.Value.WindowStart).Key;
        _attempts.Remove(oldest);
    }

    private sealed record AttemptRecord(DateTimeOffset WindowStart, int Count);
}
