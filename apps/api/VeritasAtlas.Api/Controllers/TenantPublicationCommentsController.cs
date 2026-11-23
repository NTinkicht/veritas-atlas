using System.Security.Claims;
using Microsoft.AspNetCore.Mvc;
using VeritasAtlas.Api.Infrastructure.Auth;
using VeritasAtlas.Application.Interfaces;
using VeritasAtlas.Domain.Provenance;

namespace VeritasAtlas.Api.Controllers;

/// <summary>
/// VER-P1-02 slice 2: the editorial comment history of a publication revision. The tenant and the author
/// come only from the signed token. Every lifecycle role (exactly one) may read and add, because each can
/// already read the revision; every refusal happens before the store is touched, another tenant's revision
/// answers like an unknown id, and comments never change what any role may do in the lifecycle.
/// </summary>
[ApiController]
[Route("api/v1/tenant-publication-revisions/{id:guid}/comments")]
public sealed class TenantPublicationCommentsController : ControllerBase
{
    // Worst case JSON for a valid comment: 2000 UTF-16 units each escaped as \uXXXX (6 bytes) = 12000, plus the envelope.
    public const int MaxRequestBytes = 16 * 1024;
    private readonly ITenantPublicationCommentStore _store;

    public TenantPublicationCommentsController(ITenantPublicationCommentStore store)
    {
        _store = store;
    }

    [HttpPost]
    [RequestSizeLimit(MaxRequestBytes)]
    public async Task<IActionResult> Add(Guid id, [FromBody] AddCommentRequest? request, CancellationToken cancellationToken)
    {
        var (tenantId, actor, refusal) = Authorize();
        if (refusal is not null) return refusal;
        if (request?.ClientKey is not { } key || key == Guid.Empty)
        {
            return Error(StatusCodes.Status400BadRequest, "invalid_client_key", "clientKey is a non-empty GUID chosen by the caller.");
        }

        if (TenantPublicationCommentRules.NormalizeBody(request.Body) is not { } body)
        {
            return Error(StatusCodes.Status400BadRequest, "invalid_comment",
                $"body is 1 to {TenantPublicationCommentRules.MaxBodyLength} characters without control characters.");
        }

        var result = await _store.AddAsync(tenantId, id, key, body, actor!, cancellationToken);
        return result.Outcome switch
        {
            CommentAddOutcome.Created => StatusCode(StatusCodes.Status201Created, ToResponse(result.Comment!)),
            CommentAddOutcome.Replayed => Ok(ToResponse(result.Comment!)),
            CommentAddOutcome.KeyConflict => Error(StatusCodes.Status409Conflict, "comment_key_conflict",
                "The clientKey was already used for a different comment."),
            CommentAddOutcome.LimitReached => Error(StatusCodes.Status409Conflict, "comment_limit_reached",
                $"A revision holds at most {TenantPublicationCommentRules.MaxPerRevision} comments."),
            _ => NotFoundError()
        };
    }

    [HttpGet]
    public async Task<IActionResult> List(Guid id, CancellationToken cancellationToken)
    {
        var (tenantId, _, refusal) = Authorize();
        if (refusal is not null) return refusal;
        var comments = await _store.ListAsync(tenantId, id, cancellationToken);
        return comments is null ? NotFoundError() : Ok(new CommentListResponse(comments.Select(ToResponse).ToList()));
    }

    private (Guid TenantId, string? Actor, IActionResult? Refusal) Authorize()
    {
        if (TenantClaim.GetTenantId(User) is not { } tenantId)
        {
            return (Guid.Empty, null, Error(StatusCodes.Status403Forbidden, "tenant_required",
                "The access token carries no valid organization tenant."));
        }

        var actor = User.FindFirstValue(ClaimTypes.Name);
        if (actor is null
            || TenantPublicationReviewerRules.Normalize(actor) != actor
            || actor.Length > TenantAuditEventLimits.MaxSubjectLength)
        {
            return (Guid.Empty, null, Error(StatusCodes.Status403Forbidden, "actor_required",
                "The access token carries no usable subject name."));
        }

        if (TenantPublicationRevisionsController.LifecycleRoleOf(User) == LifecycleRole.None)
        {
            return (Guid.Empty, null, Error(StatusCodes.Status403Forbidden, LifecycleDenials.RoleNotPermitted,
                "The caller's role may not use publication comments."));
        }

        return (tenantId, actor, null);
    }

    private ObjectResult NotFoundError() =>
        Error(StatusCodes.Status404NotFound, "publication_revision_not_found", "The publication revision was not found.");

    private ObjectResult Error(int status, string code, string message) =>
        StatusCode(status, new TenantSourceError(code, message));

    private static CommentResponse ToResponse(TenantPublicationCommentModel m) =>
        new(m.Id, m.RevisionId, m.ClientKey, m.Author, m.Body, m.CreatedAtUtc);
}

public sealed record AddCommentRequest(Guid? ClientKey, string? Body);

public sealed record CommentResponse(Guid Id, Guid RevisionId, Guid ClientKey, string Author, string Body, DateTime CreatedAtUtc);

public sealed record CommentListResponse(IReadOnlyList<CommentResponse> Comments);
