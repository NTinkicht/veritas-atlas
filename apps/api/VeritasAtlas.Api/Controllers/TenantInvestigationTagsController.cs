using System.Security.Claims;
using Microsoft.AspNetCore.Mvc;
using VeritasAtlas.Api.Infrastructure.Auth;
using VeritasAtlas.Application.Interfaces;

namespace VeritasAtlas.Api.Controllers;

/// <summary>
/// VER-P1-01 slice 2: private organizing tags on a tenant investigation. The tenant comes only from
/// the signed <c>tenant</c> claim and the tag owner only from the name claim. Tagging requires exactly
/// one author role (operator or analyst); listing the caller's own tags requires exactly one known
/// workflow role. Every refusal happens before the store is touched, and another tenant's or an
/// unshared investigation answers exactly like an unknown id. Tags are lowercased, never shared.
/// </summary>
[ApiController]
[Route("api/v1/tenant-investigations/{investigationId:guid}/tags")]
public sealed class TenantInvestigationTagsController : ControllerBase
{
    private static readonly string[] AuthorRoles = ["operator", "analyst"];
    private static readonly string[] ReaderRoles = ["operator", "analyst", "reviewer", "publisher", "admin"];

    private readonly ITenantInvestigationTagStore _store;
    private readonly ITenantInvestigationShareStore _shares;

    public TenantInvestigationTagsController(ITenantInvestigationTagStore store, ITenantInvestigationShareStore shares)
    {
        _store = store;
        _shares = shares;
    }

    [HttpPut("{tag}")]
    public async Task<IActionResult> Put(Guid investigationId, string tag, CancellationToken cancellationToken)
    {
        if (Authorize(AuthorRoles, "author_required", "Only an operator or analyst may tag an investigation.")
            is not { } caller) return Refusal!;
        if (Normalize(tag) is not { } normalized)
        {
            return Error(StatusCodes.Status400BadRequest, "invalid_tag",
                "A tag is 1 to 40 characters: lowercase letters and digits first, then letters, digits, '_', '.' or '-'.");
        }

        if (!await _shares.IsAuthorizedAsync(caller.TenantId, investigationId, caller.Subject, cancellationToken))
        {
            return NotFoundError();
        }

        var result = await _store.AddAsync(caller.TenantId, investigationId, normalized, caller.Subject, cancellationToken);
        return result.Outcome switch
        {
            TagAddOutcome.Added => StatusCode(StatusCodes.Status201Created, ToResponse(result.Tag!)),
            TagAddOutcome.Replayed => Ok(ToResponse(result.Tag!)),
            TagAddOutcome.InvestigationClosed => Error(StatusCodes.Status409Conflict, "investigation_closed",
                "The investigation is closed; reopen it to change it."),
            TagAddOutcome.LimitReached => Error(StatusCodes.Status409Conflict, "tag_limit_reached",
                $"At most {TenantInvestigationTagLimits.MaxPerSubjectPerInvestigation} tags are allowed per investigation."),
            _ => NotFoundError()
        };
    }

    [HttpDelete("{tag}")]
    public async Task<IActionResult> Remove(Guid investigationId, string tag, CancellationToken cancellationToken)
    {
        if (Authorize(AuthorRoles, "author_required", "Only an operator or analyst may remove a tag.")
            is not { } caller) return Refusal!;
        if (Normalize(tag) is not { } normalized)
        {
            return Error(StatusCodes.Status400BadRequest, "invalid_tag",
                "A tag is 1 to 40 characters: lowercase letters and digits first, then letters, digits, '_', '.' or '-'.");
        }

        if (!await _shares.IsAuthorizedAsync(caller.TenantId, investigationId, caller.Subject, cancellationToken))
        {
            return NotFoundError();
        }

        var outcome = await _store.RemoveAsync(caller.TenantId, investigationId, normalized, caller.Subject, cancellationToken);
        return outcome switch
        {
            TagRemoveOutcome.NotFound => NotFoundError(),
            TagRemoveOutcome.InvestigationClosed => Error(StatusCodes.Status409Conflict, "investigation_closed",
                "The investigation is closed; reopen it to change it."),
            _ => NoContent()
        };
    }

    [HttpGet]
    public async Task<IActionResult> List(Guid investigationId, CancellationToken cancellationToken)
    {
        if (Authorize(ReaderRoles, "role_not_permitted", "The caller's role may not read tags.")
            is not { } caller) return Refusal!;

        if (!await _shares.IsAuthorizedAsync(caller.TenantId, investigationId, caller.Subject, cancellationToken))
        {
            return NotFoundError();
        }

        var items = await _store.ListAsync(caller.TenantId, investigationId, caller.Subject, cancellationToken);
        return items is null
            ? NotFoundError()
            : Ok(new TenantInvestigationTagListResponse(items.Select(ToResponse).ToList()));
    }

    private static string? Normalize(string tag)
    {
        var lowered = tag.ToLowerInvariant();
        return TenantInvestigationTagRules.IsValid(lowered) ? lowered : null;
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
        Error(StatusCodes.Status404NotFound, "tenant_investigation_not_found", "The investigation was not found.");

    private ObjectResult Error(int status, string code, string message) =>
        StatusCode(status, new TenantInvestigationTagError(code, message));

    private static TenantInvestigationTagResponse ToResponse(TenantInvestigationTagModel m) =>
        new(m.InvestigationId, m.Tag, m.CreatedAtUtc);
}

public sealed record TenantInvestigationTagResponse(Guid InvestigationId, string Tag, DateTime CreatedAtUtc);

public sealed record TenantInvestigationTagListResponse(IReadOnlyList<TenantInvestigationTagResponse> Items);

public sealed record TenantInvestigationTagError(string Code, string Message);
