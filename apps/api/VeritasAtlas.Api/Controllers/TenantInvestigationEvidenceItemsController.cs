using System.Security.Claims;
using Microsoft.AspNetCore.Mvc;
using VeritasAtlas.Api.Infrastructure.Auth;
using VeritasAtlas.Application.Interfaces;

namespace VeritasAtlas.Api.Controllers;

/// <summary>
/// VER-P0-10 slice 2 (#210): collect verified evidence into a tenant investigation. The tenant
/// comes only from the signed <c>tenant</c> claim and the audit actor only from the name claim.
/// Collecting requires exactly one author role (operator or analyst); listing requires exactly
/// one known workflow role. Every refusal happens before the store is touched, and another
/// tenant's investigation or evidence answers exactly like an unknown id.
/// </summary>
[ApiController]
[Route("api/v1/tenant-investigations/{investigationId:guid}/evidence-items")]
public sealed class TenantInvestigationEvidenceItemsController : ControllerBase
{
    private static readonly string[] AuthorRoles = ["operator", "analyst"];
    private static readonly string[] ReaderRoles = ["operator", "analyst", "reviewer", "publisher", "admin"];

    private readonly ITenantInvestigationEvidenceStore _store;

    public TenantInvestigationEvidenceItemsController(ITenantInvestigationEvidenceStore store)
    {
        _store = store;
    }

    [HttpPost]
    public async Task<IActionResult> Collect(
        Guid investigationId,
        [FromBody] CollectEvidenceItemRequest? request,
        CancellationToken cancellationToken)
    {
        if (TenantClaim.GetTenantId(User) is not { } tenantId) return TenantRequired();

        var actor = User.FindFirstValue(ClaimTypes.Name);
        if (string.IsNullOrWhiteSpace(actor)
            || actor.Length > TenantInvestigationLimits.MaxSubjectLength
            || actor.Length > TenantAuditEventLimits.MaxSubjectLength
            || actor.Any(char.IsControl))
        {
            return Error(StatusCodes.Status403Forbidden, "actor_required",
                "The access token carries no usable subject name.");
        }

        if (!HasExactlyOneRoleIn(User, AuthorRoles))
        {
            return Error(StatusCodes.Status403Forbidden, "author_required",
                "Only an operator or analyst may collect evidence.");
        }

        if (request?.EvidenceVerificationId is not { } verificationId || verificationId == Guid.Empty)
        {
            return Error(StatusCodes.Status400BadRequest, "invalid_evidence_verification_id",
                "evidenceVerificationId must be the id of a verified evidence record.");
        }

        if (!TenantInvestigationEvidenceLimits.IsValidNote(request.Note))
        {
            return Error(StatusCodes.Status400BadRequest, "invalid_note",
                $"note must be omitted or 1-{TenantInvestigationEvidenceLimits.MaxNoteLength} characters, without leading or trailing whitespace or control characters.");
        }

        var result = await _store.CollectAsync(
            tenantId, investigationId, new EvidenceItemInput(verificationId, request.Note, actor), cancellationToken);

        return result.Outcome switch
        {
            EvidenceItemCollectOutcome.Collected => StatusCode(StatusCodes.Status201Created, ToResponse(result.Item!)),
            EvidenceItemCollectOutcome.Replayed => Ok(ToResponse(result.Item!)),
            EvidenceItemCollectOutcome.NoteConflict => Error(StatusCodes.Status409Conflict, "note_conflict",
                "The evidence was already collected with a different note."),
            // Anti-oracle: another tenant's ids answer exactly like unknown ones.
            _ => Error(StatusCodes.Status404NotFound, "tenant_investigation_or_evidence_not_found",
                "The investigation or evidence was not found.")
        };
    }

    [HttpGet]
    public async Task<IActionResult> List(Guid investigationId, CancellationToken cancellationToken)
    {
        if (TenantClaim.GetTenantId(User) is not { } tenantId) return TenantRequired();
        if (!HasExactlyOneRoleIn(User, ReaderRoles))
        {
            return Error(StatusCodes.Status403Forbidden, "role_not_permitted",
                "The caller's role may not read collected evidence.");
        }

        var items = await _store.ListAsync(tenantId, investigationId, cancellationToken);
        return items is null
            ? Error(StatusCodes.Status404NotFound, "tenant_investigation_or_evidence_not_found",
                "The investigation or evidence was not found.")
            : Ok(new TenantInvestigationEvidenceItemListResponse(items.Select(ToResponse).ToList()));
    }

    /// <summary>
    /// VER-P0-10 slice 3: read-only comparison of two collected items. Only ids of items in the
    /// caller tenant's investigation are accepted; anything else answers like an unknown id.
    /// </summary>
    [HttpGet("comparison")]
    public async Task<IActionResult> Compare(
        Guid investigationId, [FromQuery] Guid? left, [FromQuery] Guid? right, CancellationToken cancellationToken)
    {
        if (TenantClaim.GetTenantId(User) is not { } tenantId) return TenantRequired();
        if (!HasExactlyOneRoleIn(User, ReaderRoles))
        {
            return Error(StatusCodes.Status403Forbidden, "role_not_permitted",
                "The caller's role may not read collected evidence.");
        }

        if (left is not { } leftId || right is not { } rightId
            || leftId == Guid.Empty || rightId == Guid.Empty || leftId == rightId)
        {
            return Error(StatusCodes.Status400BadRequest, "invalid_comparison",
                "left and right must be two different collected evidence item ids.");
        }

        var comparison = await _store.CompareAsync(tenantId, investigationId, leftId, rightId, cancellationToken);
        return comparison is null
            ? Error(StatusCodes.Status404NotFound, "tenant_investigation_or_evidence_not_found",
                "The investigation or evidence was not found.")
            : Ok(comparison);
    }

    /// <summary>Fail closed: the distinct role claims name exactly one role, and it is allowed.</summary>
    private static bool HasExactlyOneRoleIn(ClaimsPrincipal user, string[] allowed)
    {
        var roles = user.FindAll(ClaimTypes.Role).Concat(user.FindAll("role"))
            .Select(c => c.Value)
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToList();
        return roles.Count == 1
            && allowed.Contains(roles[0], StringComparer.OrdinalIgnoreCase);
    }

    private ObjectResult TenantRequired() =>
        Error(StatusCodes.Status403Forbidden, "tenant_required",
            "The access token carries no valid organization tenant.");

    private ObjectResult Error(int status, string code, string message) =>
        StatusCode(status, new TenantInvestigationEvidenceItemError(code, message));

    private static TenantInvestigationEvidenceItemResponse ToResponse(TenantInvestigationEvidenceItemModel m) =>
        new(m.Id, m.InvestigationId, m.EvidenceVerificationId, m.Note, m.CollectedBySubject, m.CreatedAtUtc);
}

public sealed record CollectEvidenceItemRequest(Guid? EvidenceVerificationId, string? Note);

public sealed record TenantInvestigationEvidenceItemResponse(
    Guid Id, Guid InvestigationId, Guid EvidenceVerificationId, string? Note,
    string CollectedBySubject, DateTime CreatedAtUtc);

public sealed record TenantInvestigationEvidenceItemListResponse(
    IReadOnlyList<TenantInvestigationEvidenceItemResponse> Items);

public sealed record TenantInvestigationEvidenceItemError(string Code, string Message);
