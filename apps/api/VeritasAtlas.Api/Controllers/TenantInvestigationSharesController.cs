using System.Security.Claims;
using Microsoft.AspNetCore.Mvc;
using VeritasAtlas.Api.Infrastructure.Auth;
using VeritasAtlas.Application.Interfaces;

namespace VeritasAtlas.Api.Controllers;

/// <summary>
/// VER-P0-10 slice 6: sharing of a tenant investigation. The tenant comes only from the signed
/// <c>tenant</c> claim and the acting owner only from the name claim. Only the investigation's
/// creator, holding exactly one author role (operator or analyst), may grant, revoke and list;
/// every other caller, another tenant and an unknown id answer an identical 404. Every refusal
/// happens before the store is touched. This slice records grants only; enforcing them on the
/// other investigation endpoints is a later slice.
/// </summary>
[ApiController]
[Route("api/v1/tenant-investigations/{investigationId:guid}/shares")]
public sealed class TenantInvestigationSharesController : ControllerBase
{
    private static readonly string[] AuthorRoles = ["operator", "analyst"];

    private readonly ITenantInvestigationShareStore _store;

    public TenantInvestigationSharesController(ITenantInvestigationShareStore store)
    {
        _store = store;
    }

    [HttpPut("{grantee}")]
    public async Task<IActionResult> Grant(Guid investigationId, string grantee, CancellationToken cancellationToken)
    {
        if (Authorize() is not { } caller) return Refusal!;
        if (!TenantInvestigationShareLimits.IsValidGrantee(grantee))
        {
            return Error(StatusCodes.Status400BadRequest, "invalid_grantee",
                $"grantee must be 1-{TenantInvestigationShareLimits.MaxGranteeLength} characters without surrounding whitespace or control characters.");
        }

        var result = await _store.GrantAsync(caller.TenantId, investigationId, caller.Subject, grantee, cancellationToken);
        return result.Outcome switch
        {
            ShareGrantOutcome.Granted => StatusCode(StatusCodes.Status201Created, ToResponse(result.Share!)),
            ShareGrantOutcome.Replayed => Ok(ToResponse(result.Share!)),
            ShareGrantOutcome.SelfGrant => Error(StatusCodes.Status400BadRequest, "self_share",
                "An investigation cannot be shared with its creator."),
            _ => NotFoundError()
        };
    }

    [HttpDelete("{grantee}")]
    public async Task<IActionResult> Revoke(Guid investigationId, string grantee, CancellationToken cancellationToken)
    {
        if (Authorize() is not { } caller) return Refusal!;
        if (!TenantInvestigationShareLimits.IsValidGrantee(grantee))
        {
            return Error(StatusCodes.Status400BadRequest, "invalid_grantee",
                $"grantee must be 1-{TenantInvestigationShareLimits.MaxGranteeLength} characters without surrounding whitespace or control characters.");
        }

        var outcome = await _store.RevokeAsync(caller.TenantId, investigationId, caller.Subject, grantee, cancellationToken);
        return outcome == ShareRevokeOutcome.NotFound ? NotFoundError() : NoContent();
    }

    [HttpGet]
    public async Task<IActionResult> List(Guid investigationId, CancellationToken cancellationToken)
    {
        if (Authorize() is not { } caller) return Refusal!;

        var items = await _store.ListActiveAsync(caller.TenantId, investigationId, caller.Subject, cancellationToken);
        return items is null
            ? NotFoundError()
            : Ok(new TenantInvestigationShareListResponse(items.Select(ToResponse).ToList()));
    }

    private IActionResult? Refusal { get; set; }

    private (Guid TenantId, string Subject)? Authorize()
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
        if (held.Count != 1 || !AuthorRoles.Contains(held[0], StringComparer.OrdinalIgnoreCase))
        {
            Refusal = Error(StatusCodes.Status403Forbidden, "author_required",
                "Only an operator or analyst may manage investigation sharing.");
            return null;
        }

        return (tenantId, subject);
    }

    private ObjectResult NotFoundError() =>
        Error(StatusCodes.Status404NotFound, "tenant_investigation_not_found",
            "The investigation was not found.");

    private ObjectResult Error(int status, string code, string message) =>
        StatusCode(status, new TenantInvestigationShareError(code, message));

    private static TenantInvestigationShareResponse ToResponse(TenantInvestigationShareModel m) =>
        new(m.InvestigationId, m.GranteeSubject, m.GrantedBySubject, m.GrantedAtUtc);
}

public sealed record TenantInvestigationShareResponse(
    Guid InvestigationId, string GranteeSubject, string GrantedBySubject, DateTime GrantedAtUtc);

public sealed record TenantInvestigationShareListResponse(IReadOnlyList<TenantInvestigationShareResponse> Items);

public sealed record TenantInvestigationShareError(string Code, string Message);
