using Microsoft.EntityFrameworkCore;
using VeritasAtlas.Application.Interfaces;
using VeritasAtlas.Infrastructure.Persistence;

namespace VeritasAtlas.Infrastructure.Services;

/// <summary>
/// VER-P0-10 slice 8: the current state of an investigation is its latest state event; an
/// investigation without events is open. Write stores call <see cref="IsClosedAsync"/> after
/// resolving the investigation in the caller tenant and refuse (fail closed) while it is closed;
/// the <c>tenant_investigation_closed_guard</c> triggers enforce the same rule in the database.
/// </summary>
internal static class InvestigationStateQueries
{
    /// <summary>
    /// True when the exception is the database closed-guard trigger firing (a close committed
    /// between the application check and the write). Callers translate it to the closed outcome.
    /// </summary>
    public static bool IsClosedViolation(Exception exception)
    {
        for (var e = exception; e is not null; e = e.InnerException!)
        {
            if (e is Npgsql.PostgresException { SqlState: "P0001", MessageText: "the investigation is closed" })
            {
                return true;
            }

            if (e.InnerException is null) break;
        }

        return false;
    }

    public static async Task<bool> IsClosedAsync(
        VeritasAtlasDbContext db, Guid tenantId, Guid investigationId, CancellationToken cancellationToken)
    {
        var state = await db.TenantInvestigationStateEvents.AsNoTracking()
            .Where(x => x.TenantId == tenantId && x.InvestigationId == investigationId)
            .OrderByDescending(x => x.Version)
            .Select(x => x.State)
            .FirstOrDefaultAsync(cancellationToken);
        return state == TenantInvestigationStates.Closed;
    }
}
