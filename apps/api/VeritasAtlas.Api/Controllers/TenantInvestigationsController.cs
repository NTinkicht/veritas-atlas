using System.Security.Claims;
using Microsoft.AspNetCore.Mvc;
using VeritasAtlas.Api.Infrastructure.Auth;
using VeritasAtlas.Application.Interfaces;

namespace VeritasAtlas.Api.Controllers;

/// <summary>
/// VER-P0-10 slice 1 (#208): scoped HTTP API over the immutable tenant
/// investigation records of VER-P0-03 (#93, #97). The tenant comes only from
/// the signed <c>tenant</c> claim; the audit actor only from the name claim.
/// Creating requires exactly one author role (operator or analyst); reading
/// requires exactly one known workflow role. Every refusal happens before the
/// store is touched, and another tenant's id answers exactly like an unknown id.
/// </summary>
[ApiController]
[Route("api/v1/tenant-investigations")]
public sealed class TenantInvestigationsController : ControllerBase
{
    private static readonly string[] AuthorRoles = ["operator", "analyst"];
    private static readonly string[] ReaderRoles = ["operator", "analyst", "reviewer", "publisher", "admin"];

    private readonly ITenantInvestigationStore _store;
    private readonly ITenantInvestigationShareStore _shares;

    public TenantInvestigationsController(ITenantInvestigationStore store, ITenantInvestigationShareStore shares)
    {
        _store = store;
        _shares = shares;
    }

    [HttpPost]
    public async Task<IActionResult> Create(
        [FromBody] CreateTenantInvestigationRequest? request,
        CancellationToken cancellationToken)
    {
        if (TenantClaim.GetTenantId(User) is not { } tenantId) return TenantRequired();

        if (ActorOrRefusal() is not { } actor) return ActorRequired();

        if (!HasExactlyOneRoleIn(User, AuthorRoles))
        {
            return Error(StatusCodes.Status403Forbidden, "author_required",
                "Only an operator or analyst may open an investigation.");
        }

        if (!TenantInvestigationLimits.IsValidCreationKey(request?.CreationKey))
        {
            return Error(StatusCodes.Status400BadRequest, "invalid_creation_key",
                $"creationKey must be 1-{TenantInvestigationLimits.MaxCreationKeyLength} characters without control characters.");
        }

        if (!TenantInvestigationLimits.IsValidTitle(request!.Title))
        {
            return Error(StatusCodes.Status400BadRequest, "invalid_title",
                $"title must be 1-{TenantInvestigationLimits.MaxTitleLength} characters, without leading or trailing whitespace or control characters.");
        }

        var result = await _store.CreateAsync(
            tenantId,
            new InvestigationInput(request.CreationKey!, request.Title!, actor),
            cancellationToken);

        return result.Outcome switch
        {
            InvestigationCreateOutcome.Created => CreatedAtAction(
                nameof(GetById), new { id = result.Investigation!.Id }, ToResponse(result.Investigation)),
            InvestigationCreateOutcome.Replayed => Ok(ToResponse(result.Investigation!)),
            _ => Error(StatusCodes.Status409Conflict, "creation_key_conflict",
                "The creation key is already bound to a different investigation.")
        };
    }

    [HttpGet("{id:guid}")]
    public async Task<IActionResult> GetById(Guid id, CancellationToken cancellationToken)
    {
        if (Refuse() is { } refusal) return refusal;
        var tenantId = TenantClaim.GetTenantId(User)!.Value;
        var actor = ActorOrRefusal()!;

        // Only the creator or an active grantee may open it (VER-P0-10 slice 7). Anti-oracle:
        // another tenant's id, an unshared id and an unknown id are answered identically.
        var entry = await _shares.IsAuthorizedAsync(tenantId, id, actor, cancellationToken)
            ? await _store.GetAsync(tenantId, id, cancellationToken)
            : null;
        return entry is null
            ? Error(StatusCodes.Status404NotFound, "tenant_investigation_not_found",
                "The investigation was not found.")
            : Ok(ToResponse(entry));
    }

    [HttpGet]
    public async Task<IActionResult> List(CancellationToken cancellationToken)
    {
        if (Refuse() is { } refusal) return refusal;
        var tenantId = TenantClaim.GetTenantId(User)!.Value;

        var actor = ActorOrRefusal()!;

        var entries = await _store.ListAsync(tenantId, cancellationToken);
        var granted = await _shares.ListActiveGrantedInvestigationIdsAsync(tenantId, actor, cancellationToken);
        var visible = entries.Where(e =>
            string.Equals(e.CreatedBySubject, actor, StringComparison.Ordinal) || granted.Contains(e.Id));
        return Ok(new TenantInvestigationListResponse(visible.Select(ToResponse).ToList()));
    }

    private ObjectResult? Refuse()
    {
        if (TenantClaim.GetTenantId(User) is null) return TenantRequired();
        if (!HasExactlyOneRoleIn(User, ReaderRoles))
        {
            return Error(StatusCodes.Status403Forbidden, "role_not_permitted",
                "The caller's role may not read investigations.");
        }

        return ActorOrRefusal() is null ? ActorRequired() : null;
    }

    private string? ActorOrRefusal()
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
        Error(StatusCodes.Status403Forbidden, "actor_required",
            "The access token carries no usable subject name.");

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
        StatusCode(status, new TenantInvestigationError(code, message));

    private static TenantInvestigationResponse ToResponse(TenantInvestigationModel model) =>
        new(model.Id, model.CreationKey, model.Title, model.CreatedBySubject, model.CreatedAtUtc);
}

public sealed record CreateTenantInvestigationRequest(string? CreationKey, string? Title);

public sealed record TenantInvestigationResponse(
    Guid Id, string CreationKey, string Title, string CreatedBySubject, DateTime CreatedAtUtc);

public sealed record TenantInvestigationListResponse(IReadOnlyList<TenantInvestigationResponse> Items);

public sealed record TenantInvestigationError(string Code, string Message);
