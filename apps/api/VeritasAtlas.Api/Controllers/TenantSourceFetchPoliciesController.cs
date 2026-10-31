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

    public const string AdminRole = TenantAdminRole.Name;

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

        if (!TenantAdminRole.IsSatisfiedBy(User))
        {
            return StatusCode(StatusCodes.Status403Forbidden, new TenantSourceError(
                "admin_required",
                "Only a tenant admin may change a source fetch policy."));
        }

        // Every policy member is required: a missing one is a malformed
        // request (400), refused before the registry, never passed on as null.
        if (request is not
            {
                PolicyVersion: { } policyVersion,
                Jurisdiction: { } jurisdiction,
                SourceType: { } sourceType,
                AllowedHosts: { } allowedHosts,
                AllowedSchemes: { } allowedSchemes,
                AllowedPorts: { } allowedPorts,
                MaxRedirects: { } maxRedirects
            })
        {
            return BadRequest(new TenantSourceError(
                "invalid_fetch_policy",
                "policyVersion, jurisdiction, sourceType, allowedHosts, allowedSchemes, allowedPorts and maxRedirects are required."));
        }

        var proposed = new SourceFetchPolicy(
            policyVersion,
            jurisdiction,
            sourceType,
            allowedHosts,
            allowedSchemes,
            allowedPorts,
            maxRedirects,
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
    int? MaxRedirects,
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
