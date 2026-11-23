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
    private readonly ITenantInvestigationShareStore _shares;

    public TenantInvestigationEvidenceItemsController(
        ITenantInvestigationEvidenceStore store, ITenantInvestigationShareStore shares)
    {
        _store = store;
        _shares = shares;
    }

    [HttpPost]
    public async Task<IActionResult> Collect(
        Guid investigationId,
        [FromBody] CollectEvidenceItemRequest? request,
        CancellationToken cancellationToken)
    {
        if (TenantClaim.GetTenantId(User) is not { } tenantId) return TenantRequired();

        if (ActorOrNull() is not { } actor) return ActorRequired();

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

        // Only the investigation's creator or an active grantee may collect (anti-oracle: identical 404).
        if (!await _shares.IsAuthorizedAsync(tenantId, investigationId, actor, cancellationToken)) return NotFoundError();

        var result = await _store.CollectAsync(
            tenantId, investigationId, new EvidenceItemInput(verificationId, request.Note, actor), cancellationToken);

        return result.Outcome switch
        {
            EvidenceItemCollectOutcome.Collected => StatusCode(StatusCodes.Status201Created, ToResponse(result.Item!)),
            EvidenceItemCollectOutcome.Replayed => Ok(ToResponse(result.Item!)),
            EvidenceItemCollectOutcome.InvestigationClosed => Error(StatusCodes.Status409Conflict, "investigation_closed",
                "The investigation is closed; reopen it to change it."),
            EvidenceItemCollectOutcome.NoteConflict => Error(StatusCodes.Status409Conflict, "note_conflict",
                "The evidence was already collected with a different note."),
            // Anti-oracle: another tenant's ids answer exactly like unknown ones.
            _ => NotFoundError()
        };
    }

    /// <summary>
    /// VER-P1-01: batch import of verified evidence into one investigation, all-or-nothing and
    /// idempotent (a repeated batch replays every entry). Same author roles, share gate and
    /// anti-oracle 404 as the single collect; every refusal happens before the store is touched.
    /// </summary>
    [HttpPost("batch")]
    public async Task<IActionResult> CollectBatch(
        Guid investigationId,
        [FromBody] CollectEvidenceItemBatchRequest? request,
        CancellationToken cancellationToken)
    {
        if (TenantClaim.GetTenantId(User) is not { } tenantId) return TenantRequired();

        if (ActorOrNull() is not { } actor) return ActorRequired();

        if (!HasExactlyOneRoleIn(User, AuthorRoles))
        {
            return Error(StatusCodes.Status403Forbidden, "author_required",
                "Only an operator or analyst may collect evidence.");
        }

        var entries = request?.Items;
        if (entries is null || entries.Count < 1 || entries.Count > TenantInvestigationEvidenceLimits.MaxBatchSize)
        {
            return Error(StatusCodes.Status400BadRequest, "invalid_batch",
                $"items must hold 1-{TenantInvestigationEvidenceLimits.MaxBatchSize} entries.");
        }

        for (var i = 0; i < entries.Count; i++)
        {
            if (entries[i]?.EvidenceVerificationId is not { } id || id == Guid.Empty)
            {
                return Error(StatusCodes.Status400BadRequest, "invalid_evidence_verification_id",
                    $"items[{i}].evidenceVerificationId must be the id of a verified evidence record.");
            }

            if (!TenantInvestigationEvidenceLimits.IsValidNote(entries[i]!.Note))
            {
                return Error(StatusCodes.Status400BadRequest, "invalid_note",
                    $"items[{i}].note must be omitted or 1-{TenantInvestigationEvidenceLimits.MaxNoteLength} characters, without leading or trailing whitespace or control characters.");
            }
        }

        if (entries.Select(x => x!.EvidenceVerificationId!.Value).Distinct().Count() != entries.Count)
        {
            return Error(StatusCodes.Status400BadRequest, "duplicate_evidence_in_batch",
                "Each evidence record may appear only once per batch.");
        }

        if (!await _shares.IsAuthorizedAsync(tenantId, investigationId, actor, cancellationToken)) return NotFoundError();

        var result = await _store.CollectBatchAsync(
            tenantId, investigationId,
            entries.Select(x => new EvidenceItemInput(x!.EvidenceVerificationId!.Value, x.Note, actor)).ToList(),
            cancellationToken);

        return result.Outcome switch
        {
            EvidenceItemCollectOutcome.Collected => StatusCode(
                result.Entries!.Any(x => !x.Replayed) ? StatusCodes.Status201Created : StatusCodes.Status200OK,
                new TenantInvestigationEvidenceItemBatchResponse(
                    result.Entries!.Select(x => new TenantInvestigationEvidenceItemBatchEntryResponse(ToResponse(x.Item), x.Replayed)).ToList())),
            EvidenceItemCollectOutcome.InvestigationClosed => Error(StatusCodes.Status409Conflict, "investigation_closed",
                "The investigation is closed; reopen it to change it."),
            EvidenceItemCollectOutcome.NoteConflict => Error(StatusCodes.Status409Conflict, "note_conflict",
                $"items[{result.FailedIndex}] was already collected with a different note; nothing was imported."),
            _ => NotFoundError()
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

        if (ActorOrNull() is not { } actor) return ActorRequired();
        if (!await _shares.IsAuthorizedAsync(tenantId, investigationId, actor, cancellationToken)) return NotFoundError();

        var items = await _store.ListAsync(tenantId, investigationId, cancellationToken);
        return items is null
            ? NotFoundError()
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

        if (ActorOrNull() is not { } actor) return ActorRequired();
        if (!await _shares.IsAuthorizedAsync(tenantId, investigationId, actor, cancellationToken)) return NotFoundError();

        var comparison = await _store.CompareAsync(tenantId, investigationId, leftId, rightId, cancellationToken);
        return comparison is null
            ? NotFoundError()
            : Ok(comparison);
    }

    private string? ActorOrNull()
    {
        var actor = User.FindFirstValue(ClaimTypes.Name);
        return string.IsNullOrWhiteSpace(actor)
               || actor.Length > TenantInvestigationLimits.MaxSubjectLength
               || actor.Length > TenantAuditEventLimits.MaxSubjectLength
               || actor.Any(char.IsControl)
            ? null
            : actor;
    }

    private ObjectResult ActorRequired() =>
        Error(StatusCodes.Status403Forbidden, "actor_required", "The access token carries no usable subject name.");

    private ObjectResult NotFoundError() =>
        Error(StatusCodes.Status404NotFound, "tenant_investigation_or_evidence_not_found",
            "The investigation or evidence was not found.");

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

public sealed record CollectEvidenceItemBatchRequest(IReadOnlyList<CollectEvidenceItemRequest?>? Items);

public sealed record TenantInvestigationEvidenceItemBatchEntryResponse(
    TenantInvestigationEvidenceItemResponse Item, bool Replayed);

public sealed record TenantInvestigationEvidenceItemBatchResponse(
    IReadOnlyList<TenantInvestigationEvidenceItemBatchEntryResponse> Items);

public sealed record TenantInvestigationEvidenceItemListResponse(
    IReadOnlyList<TenantInvestigationEvidenceItemResponse> Items);

public sealed record TenantInvestigationEvidenceItemError(string Code, string Message);
