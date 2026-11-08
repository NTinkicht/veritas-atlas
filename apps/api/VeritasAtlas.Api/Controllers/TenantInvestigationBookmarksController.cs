using System.Security.Claims;
using Microsoft.AspNetCore.Mvc;
using VeritasAtlas.Api.Infrastructure.Auth;
using VeritasAtlas.Application.Interfaces;

namespace VeritasAtlas.Api.Controllers;

/// <summary>
/// VER-P0-10 slice 5: private bookmarks on collected evidence items. The tenant comes only from the
/// signed <c>tenant</c> claim and the bookmark owner only from the name claim. Bookmarking requires
/// exactly one author role (operator or analyst); listing the caller's own bookmarks requires exactly
/// one known workflow role. Every refusal happens before the store is touched, and another tenant's
/// investigation or item answers exactly like an unknown id.
/// </summary>
[ApiController]
[Route("api/v1/tenant-investigations/{investigationId:guid}")]
public sealed class TenantInvestigationBookmarksController : ControllerBase
{
    private static readonly string[] AuthorRoles = ["operator", "analyst"];
    private static readonly string[] ReaderRoles = ["operator", "analyst", "reviewer", "publisher", "admin"];

    private readonly ITenantInvestigationBookmarkStore _store;

    public TenantInvestigationBookmarksController(ITenantInvestigationBookmarkStore store)
    {
        _store = store;
    }

    [HttpPut("evidence-items/{itemId:guid}/bookmark")]
    public async Task<IActionResult> Put(Guid investigationId, Guid itemId, CancellationToken cancellationToken)
    {
        if (Authorize(AuthorRoles, "author_required", "Only an operator or analyst may bookmark evidence.")
            is not { } caller) return Refusal!;

        var result = await _store.PutAsync(caller.TenantId, investigationId, itemId, caller.Subject, cancellationToken);
        return result.Outcome switch
        {
            BookmarkPutOutcome.Added => StatusCode(StatusCodes.Status201Created, ToResponse(result.Bookmark!)),
            BookmarkPutOutcome.Replayed => Ok(ToResponse(result.Bookmark!)),
            _ => NotFoundError()
        };
    }

    [HttpDelete("evidence-items/{itemId:guid}/bookmark")]
    public async Task<IActionResult> Remove(Guid investigationId, Guid itemId, CancellationToken cancellationToken)
    {
        if (Authorize(AuthorRoles, "author_required", "Only an operator or analyst may remove a bookmark.")
            is not { } caller) return Refusal!;

        var outcome = await _store.RemoveAsync(caller.TenantId, investigationId, itemId, caller.Subject, cancellationToken);
        return outcome == BookmarkRemoveOutcome.NotFound ? NotFoundError() : NoContent();
    }

    [HttpGet("bookmarks")]
    public async Task<IActionResult> List(Guid investigationId, CancellationToken cancellationToken)
    {
        if (Authorize(ReaderRoles, "role_not_permitted", "The caller's role may not read bookmarks.")
            is not { } caller) return Refusal!;

        var items = await _store.ListAsync(caller.TenantId, investigationId, caller.Subject, cancellationToken);
        return items is null
            ? NotFoundError()
            : Ok(new TenantInvestigationBookmarkListResponse(items.Select(ToResponse).ToList()));
    }

    private IActionResult? Refusal { get; set; }

    private (Guid TenantId, string Subject)? Authorize(string[] roles, string roleCode, string roleMessage)
    {
        if (TenantClaim.GetTenantId(User) is not { } tenantId)
        {
            Refusal = Error(StatusCodes.Status403Forbidden, "tenant_required",
                "The access token carries no valid organization tenant.");
            return null;
        }

        var subject = User.FindFirstValue(ClaimTypes.Name);
        if (string.IsNullOrWhiteSpace(subject)
            || subject.Length > TenantInvestigationLimits.MaxSubjectLength
            || subject.Length > TenantAuditEventLimits.MaxSubjectLength
            || subject.Any(char.IsControl))
        {
            Refusal = Error(StatusCodes.Status403Forbidden, "actor_required",
                "The access token carries no usable subject name.");
            return null;
        }

        var held = User.FindAll(ClaimTypes.Role).Concat(User.FindAll("role"))
            .Select(c => c.Value)
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToList();
        if (held.Count != 1 || !roles.Contains(held[0], StringComparer.OrdinalIgnoreCase))
        {
            Refusal = Error(StatusCodes.Status403Forbidden, roleCode, roleMessage);
            return null;
        }

        return (tenantId, subject);
    }

    private ObjectResult NotFoundError() =>
        Error(StatusCodes.Status404NotFound, "tenant_investigation_or_item_not_found",
            "The investigation or evidence item was not found.");

    private ObjectResult Error(int status, string code, string message) =>
        StatusCode(status, new TenantInvestigationBookmarkError(code, message));

    private static TenantInvestigationBookmarkResponse ToResponse(TenantInvestigationBookmarkModel m) =>
        new(m.InvestigationId, m.EvidenceItemId, m.CreatedAtUtc);
}

public sealed record TenantInvestigationBookmarkResponse(Guid InvestigationId, Guid EvidenceItemId, DateTime CreatedAtUtc);

public sealed record TenantInvestigationBookmarkListResponse(IReadOnlyList<TenantInvestigationBookmarkResponse> Items);

public sealed record TenantInvestigationBookmarkError(string Code, string Message);
