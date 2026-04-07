using Microsoft.AspNetCore.Mvc;
using VeritasAtlas.Api.Contracts.Workflow;
using VeritasAtlas.Infrastructure.Services;

namespace VeritasAtlas.Api.Controllers;

[ApiController]
[Route("api/v1/review-workflow")]
public class ReviewWorkflowController : ControllerBase
{
    private readonly WorkflowTransitionService _workflowTransitionService;

    public ReviewWorkflowController(WorkflowTransitionService workflowTransitionService)
    {
        _workflowTransitionService = workflowTransitionService;
    }

    [HttpPost("claims/{claimId}/send-to-review")]
    public async Task<ActionResult<WorkflowTransitionResponse>> SendClaimToReview(Guid claimId, CancellationToken cancellationToken)
    {
        var result = await _workflowTransitionService.SendClaimToReviewAsync(claimId, cancellationToken);
        return Ok(new WorkflowTransitionResponse("Claim", result.Id, result.Status, DateTime.UtcNow, "Claim sent to review."));
    }

    [HttpPost("claims/{claimId}/return-for-edit")]
    public async Task<ActionResult<WorkflowTransitionResponse>> ReturnClaimForEdit(Guid claimId, CancellationToken cancellationToken)
    {
        var result = await _workflowTransitionService.ReturnClaimForEditAsync(claimId, cancellationToken);
        return Ok(new WorkflowTransitionResponse("Claim", result.Id, result.Status, DateTime.UtcNow, "Claim returned for edit."));
    }

    [HttpPost("contradictions/{id}/escalate")]
    public async Task<ActionResult<WorkflowTransitionResponse>> EscalateContradiction(Guid id, CancellationToken cancellationToken)
    {
        var result = await _workflowTransitionService.EscalateContradictionAsync(id, cancellationToken);
        return Ok(new WorkflowTransitionResponse("Contradiction", result.Id, result.Status, DateTime.UtcNow, "Contradiction escalated."));
    }

    [HttpPost("reviews/{id}/reopen")]
    public async Task<ActionResult<WorkflowTransitionResponse>> ReopenReview(Guid id, CancellationToken cancellationToken)
    {
        var result = await _workflowTransitionService.ReopenReviewAsync(id, cancellationToken);
        return Ok(new WorkflowTransitionResponse("Review", result.Id, result.Status, DateTime.UtcNow, "Review reopened."));
    }
}