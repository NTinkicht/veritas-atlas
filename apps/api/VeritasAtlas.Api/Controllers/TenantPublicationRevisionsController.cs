using System.Security.Claims;
using System.Text.RegularExpressions;
using Microsoft.AspNetCore.Mvc;
using VeritasAtlas.Api.Infrastructure.Auth;
using VeritasAtlas.Application.Interfaces;
using VeritasAtlas.Domain.Provenance;

namespace VeritasAtlas.Api.Controllers;

/// <summary>
/// VER-P0-16 slice 5: scoped HTTP access to the tenant publication revision store. The tenant and the actor come only
/// from the signed token; the lifecycle role is derived from exactly one role claim (operator/analyst author, reviewer,
/// publisher). Every other caller (admin, no role, two roles, unknown role) is refused before the store is touched.
/// Legality, self-approval denial, version races and idempotency are decided by the store through the pure rules.
/// Another tenant's revision answers exactly like an unknown one (404). This controller exposes no public-reader route.
/// </summary>
[ApiController]
[Route("api/v1/tenant-publication-revisions")]
public sealed partial class TenantPublicationRevisionsController : ControllerBase
{
    public const int MaxRequestBytes = 4 * 1024;
    public const int MaxVisibilityRequestBytes = 256 * 1024;
    private const int MaxOperationIdLength = 200;

    private readonly ITenantPublicationRevisionStore _store;

    public TenantPublicationRevisionsController(ITenantPublicationRevisionStore store)
    {
        _store = store;
    }

    [GeneratedRegex(@"\A[0-9a-f]{64}\z")]
    private static partial Regex DigestPattern();

    [HttpPost]
    [RequestSizeLimit(MaxRequestBytes)]
    public async Task<IActionResult> CreateDraft([FromBody] CreateDraftRequest? request, CancellationToken cancellationToken)
    {
        if (Authorize() is { } refusal) return refusal;
        var (tenantId, actor, role) = Caller();
        if (role != LifecycleRole.Analyst)
        {
            return Forbidden(LifecycleDenials.RoleNotPermitted, "Only an author may create a draft.");
        }

        if (request is not { ContentDigest: { } digest, PolicyPermitsPublicDisplay: { } permits }
            || !DigestPattern().IsMatch(digest))
        {
            return BadRequest(new TenantSourceError(
                "invalid_publication_revision",
                "contentDigest (64 lowercase hex characters) and policyPermitsPublicDisplay are required."));
        }

        var created = await _store.CreateDraftAsync(tenantId, actor, digest, permits, cancellationToken);
        return Created($"/api/v1/tenant-publication-revisions/{created.Id}", ToResponse(created));
    }

    [HttpGet("{id:guid}")]
    public async Task<IActionResult> Get(Guid id, CancellationToken cancellationToken)
    {
        if (Authorize() is { } refusal) return refusal;
        var (tenantId, _, _) = Caller();
        var revision = await _store.GetAsync(tenantId, id, cancellationToken);
        return revision is null ? NotFoundRevision() : Ok(ToResponse(revision));
    }

    [HttpPost("{id:guid}/transitions")]
    [RequestSizeLimit(MaxRequestBytes)]
    public async Task<IActionResult> Transition(
        Guid id, [FromBody] TransitionRequest? request, CancellationToken cancellationToken)
    {
        if (Authorize() is { } refusal) return refusal;
        var (tenantId, actor, role) = Caller();

        if (request is not { Action: { } actionName, ExpectedVersion: { } expected, OperationId: { } operationId }
            || !Enum.TryParse<LifecycleAction>(actionName, ignoreCase: false, out var action)
            || !Enum.IsDefined(action)
            || expected < 0
            || string.IsNullOrWhiteSpace(operationId)
            || operationId.Length > MaxOperationIdLength
            || operationId.Any(char.IsControl))
        {
            return BadRequest(new TenantSourceError(
                "invalid_transition_request",
                "action, expectedVersion (>= 0) and operationId (1-200 printable characters) are required."));
        }

        var result = await _store.TransitionAsync(
            tenantId, id, actor, role, action, expected, operationId, cancellationToken);
        return result.Outcome switch
        {
            PublicationTransitionOutcome.NotFound => NotFoundRevision(),
            PublicationTransitionOutcome.ConcurrentUpdate => Conflict(new TenantSourceError(
                "concurrent_update", "The revision changed concurrently; read it again and retry.")),
            PublicationTransitionOutcome.Denied => result.DenialCode is GuardDenials.StaleVersion
                or GuardDenials.OperationConflict
                    ? Conflict(new TenantSourceError(result.DenialCode, "The request conflicts with the current revision."))
                    : Forbidden(result.DenialCode ?? "denied", "The transition is not permitted."),
            _ => Ok(new TransitionResponse(
                result.Outcome == PublicationTransitionOutcome.Replayed,
                ToResponse(result.Revision!))),
        };
    }

    /// <summary>
    /// Publisher-only audited write of the public-reader facts (slice 7, #202). Only legal while the revision is Approved or
    /// Published; unrestricting needs the policy permit and a well-formed citation. Tenant and actor come from the token.
    /// </summary>
    [HttpPut("{id:guid}/visibility")]
    [RequestSizeLimit(MaxVisibilityRequestBytes)]
    public async Task<IActionResult> SetVisibility(
        Guid id, [FromBody] VisibilityRequest? request, CancellationToken cancellationToken)
    {
        if (Authorize() is { } refusal) return refusal;
        var (tenantId, actor, role) = Caller();
        if (role != LifecycleRole.Publisher)
        {
            return Forbidden(LifecycleDenials.RoleNotPermitted, "Only a publisher may set public visibility.");
        }

        if (request is not
            {
                ExpectedFactsVersion: { } expected, OperationId: { } operationId, Restricted: { } restricted,
                Superseded: { } superseded, FreshnessRequired: { } freshnessRequired, HistoricalLabelPresent: { } historical,
                Citations: { } citations
            }
            || !Enum.TryParse<EvidenceCurrency>(request.EvidenceCurrency, ignoreCase: false, out var currency)
            || !Enum.IsDefined(currency)
            || expected < 0
            || string.IsNullOrWhiteSpace(operationId) || operationId.Length > MaxOperationIdLength
            || operationId.Any(char.IsControl)
            || operationId.StartsWith(PublicVisibilityRules.ResetOperationPrefix, StringComparison.Ordinal)
            || citations.Count > PublicVisibilityRules.MaxCitations
            || citations.Any(c => c is not { CitationLabel: not null, Title: not null, Quote: not null, SourceName: not null, FreshnessLabel: not null }))
        {
            return BadRequest(new TenantSourceError(
                "invalid_visibility_request",
                "expectedFactsVersion, operationId, restricted, superseded, freshnessRequired, evidenceCurrency, historicalLabelPresent and citations are required."));
        }

        var facts = new PublicVisibilityFactsInput(
            restricted, superseded, request.ExpiresAt, freshnessRequired, currency, historical,
            citations.Select(c => new PublicCitationProjection(
                c.CitationLabel!, c.Title!, c.Quote!, c.SourceName!, c.AsOf, c.EffectiveDate, c.FreshnessLabel!)).ToList());
        var result = await _store.SetVisibilityAsync(
            tenantId, id, actor, role, facts, expected, operationId, cancellationToken);
        return result.Outcome switch
        {
            VisibilityOutcome.NotFound => NotFoundRevision(),
            VisibilityOutcome.ConcurrentUpdate => Conflict(new TenantSourceError(
                "concurrent_update", "The revision changed concurrently; read it again and retry.")),
            VisibilityOutcome.Denied => result.DenialCode is GuardDenials.StaleVersion or GuardDenials.OperationConflict
                ? Conflict(new TenantSourceError(result.DenialCode, "The request conflicts with the current revision."))
                : result.DenialCode is VisibilityDenials.InvalidFacts or VisibilityDenials.CitationsRequired
                    ? BadRequest(new TenantSourceError(result.DenialCode, "The visibility facts are not valid."))
                    : Forbidden(result.DenialCode ?? "denied", "The visibility change is not permitted."),
            _ => Ok(new TransitionResponse(
                result.Outcome == VisibilityOutcome.Replayed, ToResponse(result.Revision!))),
        };
    }

    private IActionResult? Authorize()
    {
        if (TenantClaim.GetTenantId(User) is null)
        {
            return Forbidden("tenant_required", "The access token carries no valid organization tenant.");
        }

        var actor = User.FindFirstValue(ClaimTypes.Name);
        if (string.IsNullOrWhiteSpace(actor)
            || actor.Length > TenantAuditEventLimits.MaxSubjectLength
            || actor.Any(char.IsControl))
        {
            return Forbidden("actor_required", "The access token carries no usable subject name.");
        }

        return LifecycleRoleOf(User) == LifecycleRole.None
            ? Forbidden(LifecycleDenials.RoleNotPermitted, "The caller's role has no publication workflow permission.")
            : null;
    }

    private (Guid TenantId, string Actor, LifecycleRole Role) Caller() =>
        (TenantClaim.GetTenantId(User)!.Value, User.FindFirstValue(ClaimTypes.Name)!, LifecycleRoleOf(User));

    /// <summary>Fail closed: exactly one distinct role claim that maps to a lifecycle role.</summary>
    internal static LifecycleRole LifecycleRoleOf(ClaimsPrincipal user)
    {
        var roles = user.FindAll(ClaimTypes.Role).Concat(user.FindAll("role"))
            .Select(c => c.Value).Distinct(StringComparer.OrdinalIgnoreCase).ToList();
        if (roles.Count != 1) return LifecycleRole.None;
        return roles[0].ToLowerInvariant() switch
        {
            "operator" or "analyst" => LifecycleRole.Analyst,
            "reviewer" => LifecycleRole.Reviewer,
            "publisher" => LifecycleRole.Publisher,
            _ => LifecycleRole.None,
        };
    }

    private ObjectResult Forbidden(string code, string message) =>
        StatusCode(StatusCodes.Status403Forbidden, new TenantSourceError(code, message));

    private NotFoundObjectResult NotFoundRevision() =>
        NotFound(new TenantSourceError("publication_revision_not_found", "The publication revision was not found."));

    private static RevisionResponse ToResponse(TenantPublicationRevisionModel m) =>
        new(m.Id, m.AuthorId, m.ContentDigest, m.ApprovedDigest, m.PolicyPermitsPublicDisplay, m.State.ToString(), m.Version, m.FactsVersion);
}

public sealed record CreateDraftRequest(string? ContentDigest, bool? PolicyPermitsPublicDisplay);

public sealed record TransitionRequest(string? Action, long? ExpectedVersion, string? OperationId);

public sealed record RevisionResponse(
    Guid Id, string AuthorId, string ContentDigest, string? ApprovedDigest, bool PolicyPermitsPublicDisplay,
    string State, long Version, long FactsVersion = 0);

public sealed record TransitionResponse(bool Replayed, RevisionResponse Revision);

public sealed record VisibilityRequest(
    long? ExpectedFactsVersion, string? OperationId, bool? Restricted, bool? Superseded, DateTimeOffset? ExpiresAt,
    bool? FreshnessRequired, string? EvidenceCurrency, bool? HistoricalLabelPresent, List<VisibilityCitationRequest>? Citations);

public sealed record VisibilityCitationRequest(
    string? CitationLabel, string? Title, string? Quote, string? SourceName, DateTimeOffset? AsOf,
    DateTimeOffset? EffectiveDate, string? FreshnessLabel);
