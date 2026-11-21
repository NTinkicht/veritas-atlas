using System.Net.Sockets;
using Npgsql;

namespace VeritasAtlas.Api.Infrastructure;

/// <summary>
/// VER-P0-19: pure classifier telling "the database cannot be reached or is
/// refusing connections" (a retryable 503) apart from application or data
/// errors (a 500). Walks the whole exception chain.
/// </summary>
public static class DatabaseUnavailable
{
    // 08xxx connection exception; 53300 too_many_connections; 57P01-57P03
    // admin_shutdown / crash_shutdown / cannot_connect_now.
    private static readonly HashSet<string> UnavailableSqlStates =
        new(StringComparer.Ordinal) { "53300", "57P01", "57P02", "57P03" };

    public static bool Is(Exception? exception)
    {
        var depth = 0;
        for (var current = exception; current is not null && depth < 16; current = current.InnerException, depth++)
        {
            if (current is PostgresException pg)
            {
                if (pg.SqlState.StartsWith("08", StringComparison.Ordinal) || UnavailableSqlStates.Contains(pg.SqlState))
                {
                    return true;
                }

                continue;
            }

            if (current is NpgsqlException npgsql)
            {
                if (npgsql.IsTransient || npgsql.InnerException is SocketException or IOException or TimeoutException)
                {
                    return true;
                }
            }
        }

        return false;
    }
}
