using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using VeritasAtlas.Api.Contracts.Workflow;
using VeritasAtlas.Api.Infrastructure.Auth;
using VeritasAtlas.Infrastructure.Services;

namespace VeritasAtlas.Api.Controllers;

[ApiController]
[Authorize]
[Route("api/v1/review-workflow")]
public class ReviewWorkflowController : ControllerBase
{
    private readonly WorkflowOrchestratorService _workflowOrchestratorService;
    private readonly AuthRequestContext _authRequestContext;

    public ReviewWorkflowController(
        WorkflowOrchestratorService workflowOrchestratorService,
        AuthRequestContext authRequestContext)
    {
        _workflowOrchestratorService = workflowOrchestratorService;
        _authRequestContext = authRequestContext;
    }

    [HttpPost("claims/{claimId}/send-to-review")]
    public async Task<ActionResult<WorkflowTransitionResponse>> SendClaimToReview(Guid claimId, CancellationToken cancellationToken)
    {
        return await ExecuteTransition(
            "Claim",
            claimId,
            () => _workflowOrchestratorService.SendClaimToReviewAsync(claimId, _authRequestContext.GetRole(HttpContext), cancellationToken),
            "Claim sent to review.");
    }

    [HttpPost("claims/{claimId}/return-for-edit")]
    public async Task<ActionResult<WorkflowTransitionResponse>> ReturnClaimForEdit(Guid claimId, CancellationToken cancellationToken)
    {
        return await ExecuteTransition(
            "Claim",
            claimId,
            () => _workflowOrchestratorService.ReturnClaimForEditAsync(claimId, _authRequestContext.GetRole(HttpContext), cancellationToken),
            "Claim returned for edit.");
    }

    [HttpPost("contradictions/{id}/escalate")]
    public async Task<ActionResult<WorkflowTransitionResponse>> EscalateContradiction(Guid id, CancellationToken cancellationToken)
    {
        return await ExecuteTransition(
            "Contradiction",
            id,
            () => _workflowOrchestratorService.EscalateContradictionAsync(id, _authRequestContext.GetRole(HttpContext), cancellationToken),
            "Contradiction escalated.");
    }

    [HttpPost("reviews/{id}/reopen")]
    public async Task<ActionResult<WorkflowTransitionResponse>> ReopenReview(Guid id, CancellationToken cancellationToken)
    {
        return await ExecuteTransition(
            "Review",
            id,
            () => _workflowOrchestratorService.ReopenReviewAsync(id, _authRequestContext.GetRole(HttpContext), cancellationToken),
            "Review reopened.");
    }

    private async Task<ActionResult<WorkflowTransitionResponse>> ExecuteTransition(
        string entityType,
        Guid entityId,
        Func<Task<(Guid Id, string Status)>> action,
        string message)
    {
        try
        {
            var result = await action();
            return Ok(new WorkflowTransitionResponse(entityType, result.Id, result.Status, DateTime.UtcNow, message));
        }
        catch (UnauthorizedAccessException ex)
        {
            return StatusCode(StatusCodes.Status403Forbidden, ex.Message);
        }
        catch (InvalidOperationException ex)
        {
            return BadRequest(new WorkflowValidationFailureResponse(entityType, entityId, message, ex.Message, DateTime.UtcNow));
        }
    }
}