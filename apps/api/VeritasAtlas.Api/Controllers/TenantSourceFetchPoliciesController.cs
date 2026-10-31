using System.Security.Claims;
using System.Text.Json.Serialization;
using Microsoft.AspNetCore.Mvc;
using VeritasAtlas.Api.Infrastructure.Auth;
using VeritasAtlas.Application.Interfaces;
using VeritasAtlas.Domain.Provenance;

namespace VeritasAtlas.Api.Controllers;

/// <summary>
/// VER-P0-04 slice 5c (#123): HTTP access to a tenant source's fetch policy
/// (the allowed-host boundary of the future fetcher). The tenant comes only
/// from the signed <c>tenant</c> claim. Any authenticated tenant member may
/// read the current revision; only a caller whose single role claim is
/// <c>admin</c> may propose a revision or approve a widening (decision B in
/// #123), and every other caller is refused before the registry is touched.
/// Another tenant's source answers exactly like an unknown one (404).
/// </summary>
[ApiController]
[Route("api/v1/tenant-sources/{id:guid}/fetch-policy")]
public sealed class TenantSourceFetchPoliciesController : ControllerBase
{
    /// <summary>Upper bound for a policy request body.</summary>
    public const int MaxRequestBytes = 16 * 1024;

    public const string AdminRole = "admin";

    private readonly ITenantSourceFetchPolicyRegistry _registry;

    public TenantSourceFetchPoliciesController(ITenantSourceFetchPolicyRegistry registry)
    {
        _registry = registry;
    }

    [HttpGet]
    public async Task<IActionResult> Get(Guid id, CancellationToken cancellationToken)
    {
        if (TenantClaim.GetTenantId(User) is not { } tenantId) return TenantRequired();

        var current = await _registry.GetCurrentAsync(tenantId, id, cancellationToken);
        return current is null ? PolicyNotFound() : Ok(ToResponse(current));
    }

    [HttpPut]
    [RequestSizeLimit(MaxRequestBytes)]
    public async Task<IActionResult> Put(
        Guid id,
        [FromBody] ProposeFetchPolicyRequest? request,
        CancellationToken cancellationToken)
    {
        if (TenantClaim.GetTenantId(User) is not { } tenantId) return TenantRequired();

        var actor = User.FindFirstValue(ClaimTypes.Name);
        if (string.IsNullOrWhiteSpace(actor)
            || actor.Length > TenantAuditEventLimits.MaxSubjectLength
            || actor.Any(char.IsControl))
        {
            return StatusCode(StatusCodes.Status403Forbidden, new TenantSourceError(
                "actor_required",
                "The access token carries no usable subject name."));
        }

        if (!IsAdmin(User))
        {
            return StatusCode(StatusCodes.Status403Forbidden, new TenantSourceError(
                "admin_required",
                "Only a tenant admin may change a source fetch policy."));
        }

        if (request is null)
        {
            return BadRequest(new TenantSourceError(
                "invalid_fetch_policy",
                "A fetch policy body is required."));
        }

        var proposed = new SourceFetchPolicy(
            request.PolicyVersion!,
            request.Jurisdiction!,
            request.SourceType!,
            request.AllowedHosts!,
            request.AllowedSchemes!,
            request.AllowedPorts!,
            request.MaxRedirects,
            request.PlaintextHttpException);

        var result = await _registry.ProposeAsync(
            tenantId, id, proposed, request.WideningApproved, actor, cancellationToken);

        return result.Outcome switch
        {
            SourceFetchPolicyProposalOutcome.Created =>
                Ok(new FetchPolicyProposalResponse(true, ToResponse(result.Policy!))),
            SourceFetchPolicyProposalOutcome.Unchanged =>
                Ok(new FetchPolicyProposalResponse(false, ToResponse(result.Policy!))),
            SourceFetchPolicyProposalOutcome.Refused =>
                UnprocessableEntity(new FetchPolicyRefusal(
                    "fetch_policy_refused",
                    "The fetch policy was refused by connector admission or the source change policy.",
                    result.Reasons)),
            SourceFetchPolicyProposalOutcome.SourceNotFound => PolicyNotFound(),
            SourceFetchPolicyProposalOutcome.Conflict =>
                Conflict(new TenantSourceError(
                    "fetch_policy_conflict",
                    "Another revision was written concurrently; read the current policy and retry.")),
            _ => throw new InvalidOperationException($"Unexpected outcome {result.Outcome}.")
        };
    }

    /// <summary>
    /// Fail closed: the token's role claims (issued tokens carry the same role
    /// as both <see cref="ClaimTypes.Role"/> and <c>role</c>) must name exactly
    /// one role, <c>admin</c> (case-insensitive). No role, another role or two
    /// different roles are refused.
    /// </summary>
    internal static bool IsAdmin(ClaimsPrincipal user)
    {
        var roles = user.FindAll(ClaimTypes.Role)
            .Concat(user.FindAll("role"))
            .Select(c => c.Value)
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToList();
        return roles.Count == 1
            && string.Equals(roles[0], AdminRole, StringComparison.OrdinalIgnoreCase);
    }

    // Unknown, foreign-tenant and policy-less sources share one body.
    private NotFoundObjectResult PolicyNotFound() =>
        NotFound(new TenantSourceError("fetch_policy_not_found", "The fetch policy was not found."));

    private ObjectResult TenantRequired() =>
        StatusCode(StatusCodes.Status403Forbidden, new TenantSourceError(
            "tenant_required",
            "The access token carries no valid organization tenant."));

    private static FetchPolicyResponse ToResponse(TenantSourceFetchPolicyModel model) =>
        new(model.SourceRegistryEntryId,
            model.Revision,
            model.Policy.PolicyVersion,
            model.Policy.Jurisdiction,
            model.Policy.SourceType,
            model.Policy.AllowedHosts.ToList(),
            model.Policy.AllowedSchemes.ToList(),
            model.Policy.AllowedPorts.ToList(),
            model.Policy.MaxRedirects,
            model.Policy.PlaintextHttpException,
            model.WideningApproved,
            model.CreatedAtUtc);
}

[JsonUnmappedMemberHandling(JsonUnmappedMemberHandling.Disallow)]
public sealed record ProposeFetchPolicyRequest(
    string? PolicyVersion,
    string? Jurisdiction,
    string? SourceType,
    List<string>? AllowedHosts,
    List<string>? AllowedSchemes,
    List<int>? AllowedPorts,
    int MaxRedirects,
    bool PlaintextHttpException = false,
    bool WideningApproved = false);

public sealed record FetchPolicyResponse(
    Guid SourceId,
    int Revision,
    string PolicyVersion,
    string Jurisdiction,
    string SourceType,
    IReadOnlyList<string> AllowedHosts,
    IReadOnlyList<string> AllowedSchemes,
    IReadOnlyList<int> AllowedPorts,
    int MaxRedirects,
    bool PlaintextHttpException,
    bool WideningApproved,
    DateTime CreatedAtUtc);

public sealed record FetchPolicyProposalResponse(bool Changed, FetchPolicyResponse Policy);

public sealed record FetchPolicyRefusal(string Code, string Message, IReadOnlyList<string> Reasons);
