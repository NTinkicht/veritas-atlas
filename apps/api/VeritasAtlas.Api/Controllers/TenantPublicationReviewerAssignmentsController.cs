using System.Security.Claims;
using Microsoft.AspNetCore.Mvc;
using VeritasAtlas.Api.Infrastructure.Auth;
using VeritasAtlas.Application.Interfaces;
using VeritasAtlas.Domain.Provenance;

namespace VeritasAtlas.Api.Controllers;

/// <summary>
/// VER-P1-02 slice 1: the reviewer a publisher assigns to a publication revision. The tenant and the actor
/// come only from the signed token. Assigning and clearing require exactly the publisher role; reading
/// requires exactly one lifecycle role. Every refusal happens before the store is touched, another tenant's
/// revision answers like an unknown id, and the assignment is routing metadata that never changes what any
/// role may do in the lifecycle (the existing transitions stay the only gate).
/// </summary>
[ApiController]
[Route("api/v1/tenant-publication-revisions/{id:guid}/reviewer")]
public sealed class TenantPublicationReviewerAssignmentsController : ControllerBase
{
    public const int MaxRequestBytes = 4 * 1024;
    private readonly ITenantPublicationReviewerAssignmentStore _store;

    public TenantPublicationReviewerAssignmentsController(ITenantPublicationReviewerAssignmentStore store)
    {
        _store = store;
    }

    [HttpPut]
    [RequestSizeLimit(MaxRequestBytes)]
    public async Task<IActionResult> Assign(Guid id, [FromBody] ReviewerAssignmentRequest? request, CancellationToken cancellationToken)
    {
        if (Authorize(publisherOnly: true) is not { } caller) return Refusal!;
        if (TenantPublicationReviewerRules.Normalize(request?.Reviewer) is not { } reviewer)
        {
            return Error(StatusCodes.Status400BadRequest, "invalid_reviewer",
                "reviewer is a subject name of 1 to 200 printable characters.");
        }

        var result = await _store.AssignAsync(caller.TenantId, id, reviewer, caller.Actor, cancellationToken);
        return result.Outcome switch
        {
            ReviewerAssignOutcome.Created => StatusCode(StatusCodes.Status201Created, ToResponse(result.Assignment!)),
            ReviewerAssignOutcome.Changed or ReviewerAssignOutcome.Replayed => Ok(ToResponse(result.Assignment!)),
            ReviewerAssignOutcome.SelfReviewDenied => Error(StatusCodes.Status403Forbidden, "self_review_denied",
                "The revision author cannot be assigned as its reviewer."),
            ReviewerAssignOutcome.RevisionClosed => ClosedError(),
            _ => NotFoundError()
        };
    }

    [HttpDelete]
    public async Task<IActionResult> Unassign(Guid id, CancellationToken cancellationToken)
    {
        if (Authorize(publisherOnly: true) is not { } caller) return Refusal!;
        var outcome = await _store.UnassignAsync(caller.TenantId, id, caller.Actor, cancellationToken);
        return outcome switch
        {
            ReviewerUnassignOutcome.NotFound => NotFoundError(),
            ReviewerUnassignOutcome.RevisionClosed => ClosedError(),
            _ => NoContent()
        };
    }

    [HttpGet]
    public async Task<IActionResult> Get(Guid id, CancellationToken cancellationToken)
    {
        if (Authorize(publisherOnly: false) is not { } caller) return Refusal!;
        var result = await _store.GetAsync(caller.TenantId, id, cancellationToken);
        if (!result.RevisionFound) return NotFoundError();
        return result.Assignment is null
            ? Error(StatusCodes.Status404NotFound, "reviewer_not_assigned", "No reviewer is assigned to the revision.")
            : Ok(ToResponse(result.Assignment));
    }

    private IActionResult? Refusal { get; set; }

    private (Guid TenantId, string Actor)? Authorize(bool publisherOnly)
    {
        if (TenantClaim.GetTenantId(User) is not { } tenantId)
        {
            Refusal = Error(StatusCodes.Status403Forbidden, "tenant_required",
                "The access token carries no valid organization tenant.");
            return null;
        }

        var actor = User.FindFirstValue(ClaimTypes.Name);
        if (TenantPublicationReviewerRules.Normalize(actor) != actor
            || actor is null
            || actor.Length > TenantAuditEventLimits.MaxSubjectLength)
        {
            Refusal = Error(StatusCodes.Status403Forbidden, "actor_required",
                "The access token carries no usable subject name.");
            return null;
        }

        var role = TenantPublicationRevisionsController.LifecycleRoleOf(User);
        if (role == LifecycleRole.None || (publisherOnly && role != LifecycleRole.Publisher))
        {
            Refusal = Error(StatusCodes.Status403Forbidden, LifecycleDenials.RoleNotPermitted,
                "The caller's role may not do this on reviewer assignments.");
            return null;
        }

        return (tenantId, actor);
    }

    private ObjectResult ClosedError() =>
        Error(StatusCodes.Status409Conflict, "revision_closed", "The revision is already decided; its reviewer can no longer change.");

    private ObjectResult NotFoundError() =>
        Error(StatusCodes.Status404NotFound, "publication_revision_not_found", "The publication revision was not found.");

    private ObjectResult Error(int status, string code, string message) =>
        StatusCode(status, new TenantSourceError(code, message));

    private static ReviewerAssignmentResponse ToResponse(TenantPublicationReviewerAssignmentModel m) =>
        new(m.RevisionId, m.Reviewer, m.AssignedBy, m.AssignedAtUtc);
}

public sealed record ReviewerAssignmentRequest(string? Reviewer);

public sealed record ReviewerAssignmentResponse(Guid RevisionId, string Reviewer, string AssignedBy, DateTime AssignedAtUtc);
