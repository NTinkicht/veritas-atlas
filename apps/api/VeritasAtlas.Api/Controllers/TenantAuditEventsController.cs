using System.Security.Claims;
using Microsoft.AspNetCore.Mvc;
using VeritasAtlas.Api.Infrastructure.Auth;
using VeritasAtlas.Application.Interfaces;

namespace VeritasAtlas.Api.Controllers;

/// <summary>
/// VER-P0-18 (#368): read-only list of the append-only tenant audit ledger, so an admin can verify
/// what was recorded without database access. Same fail-closed gate as the sibling admin reads: the
/// tenant comes only from the signed <c>tenant</c> claim, the caller needs a usable subject name and a
/// single <c>admin</c> role, and every other caller is refused before the store is touched. The
/// optional <c>entityType</c>/<c>entityId</c> filters are validated like the writer's identifiers
/// (an <c>entityId</c> needs an <c>entityType</c>). Only the newest <see cref="MaxListed"/> events are
/// returned, oldest first, and the idempotency event key is never exposed. There is no write verb.
/// </summary>
[ApiController]
[Route("api/v1/tenant-audit-events")]
public sealed class TenantAuditEventsController : ControllerBase
{
    private readonly ITenantAuditEventStore _store;

    public TenantAuditEventsController(ITenantAuditEventStore store)
    {
        _store = store;
    }

    /// <summary>Server-side ceiling on one list response; the newest rows win.</summary>
    public const int MaxListed = 200;

    [HttpGet]
    public async Task<IActionResult> List(
        [FromQuery] string? entityType, [FromQuery] string? entityId, CancellationToken cancellationToken)
    {
        if (TenantClaim.GetTenantId(User) is not { } tenantId)
            return Error(StatusCodes.Status403Forbidden, "tenant_required",
                "The access token carries no valid organization tenant.");

        var actor = User.FindFirstValue(ClaimTypes.Name);
        if (string.IsNullOrWhiteSpace(actor)
            || actor.Length > TenantAuditEventLimits.MaxSubjectLength
            || actor.Any(char.IsControl))
            return Error(StatusCodes.Status403Forbidden, "actor_required",
                "The access token carries no usable subject name.");

        if (!TenantAdminRole.IsSatisfiedBy(User))
            return Error(StatusCodes.Status403Forbidden, "admin_required",
                "Only a tenant admin may read audit events.");

        if (entityType is not null && !IsValid(entityType, TenantAuditEventLimits.MaxEntityTypeLength))
            return Error(StatusCodes.Status400BadRequest, "entity_type_invalid",
                "entityType must be a non-blank identifier without control characters.");
        if (entityId is not null && !IsValid(entityId, TenantAuditEventLimits.MaxEntityIdLength))
            return Error(StatusCodes.Status400BadRequest, "entity_id_invalid",
                "entityId must be a non-blank identifier without control characters.");
        if (entityId is not null && entityType is null)
            return Error(StatusCodes.Status400BadRequest, "entity_type_required",
                "An entityId filter requires an entityType.");

        var rows = await _store.ListRecentAsync(tenantId, entityType, entityId, MaxListed, cancellationToken);
        return Ok(rows
            .Select(r => new AuditEventResponse(
                r.Id, r.Action, r.EntityType, r.EntityId, r.ActorSubject, r.OccurredAtUtc))
            .ToList());
    }

    private static bool IsValid(string value, int maxLength) =>
        !string.IsNullOrWhiteSpace(value) && value.Length <= maxLength && !value.Any(char.IsControl);

    private ObjectResult Error(int status, string code, string message) =>
        StatusCode(status, new TenantSourceError(code, message));
}

public sealed record AuditEventResponse(
    Guid Id, string Action, string EntityType, string EntityId, string ActorSubject, DateTime OccurredAtUtc);
