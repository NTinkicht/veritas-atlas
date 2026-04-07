using Microsoft.AspNetCore.Mvc;
using VeritasAtlas.Api.Contracts.Workflow;
using VeritasAtlas.Infrastructure.Services;

namespace VeritasAtlas.Api.Controllers;

[ApiController]
[Route("api/v1/actions")]
public class ActionsController : ControllerBase
{
    private readonly WorkflowTransitionService _workflowTransitionService;

    public ActionsController(WorkflowTransitionService workflowTransitionService)
    {
        _workflowTransitionService = workflowTransitionService;
    }

    [HttpPost("cases/{caseId}/submit")]
    public async Task<ActionResult<WorkflowTransitionResponse>> SubmitCase(Guid caseId, CancellationToken cancellationToken)
    {
        var result = await _workflowTransitionService.SubmitCaseAsync(caseId, cancellationToken);
        return Ok(new WorkflowTransitionResponse("Case", result.Id, result.Status, DateTime.UtcNow, "Case submitted."));
    }

    [HttpPost("cases/{caseId}/approve")]
    public async Task<ActionResult<WorkflowTransitionResponse>> ApproveCase(Guid caseId, CancellationToken cancellationToken)
    {
        var result = await _workflowTransitionService.ApproveCaseAsync(caseId, cancellationToken);
        return Ok(new WorkflowTransitionResponse("Case", result.Id, result.Status, DateTime.UtcNow, "Case approved."));
    }

    [HttpPost("cases/{caseId}/reject")]
    public async Task<ActionResult<WorkflowTransitionResponse>> RejectCase(Guid caseId, CancellationToken cancellationToken)
    {
        var result = await _workflowTransitionService.RejectCaseAsync(caseId, cancellationToken);
        return Ok(new WorkflowTransitionResponse("Case", result.Id, result.Status, DateTime.UtcNow, "Case rejected."));
    }

    [HttpPost("contradictions/{id}/resolve")]
    public async Task<ActionResult<WorkflowTransitionResponse>> ResolveContradiction(Guid id, CancellationToken cancellationToken)
    {
        var result = await _workflowTransitionService.ResolveContradictionAsync(id, cancellationToken);
        return Ok(new WorkflowTransitionResponse("Contradiction", result.Id, result.Status, DateTime.UtcNow, "Contradiction resolved."));
    }

    [HttpPost("reviews/{id}/complete")]
    public async Task<ActionResult<WorkflowTransitionResponse>> CompleteReview(Guid id, CancellationToken cancellationToken)
    {
        var result = await _workflowTransitionService.CompleteReviewAsync(id, cancellationToken);
        return Ok(new WorkflowTransitionResponse("Review", result.Id, result.Status, DateTime.UtcNow, "Review completed."));
    }

    [HttpPost("seed/lifecycle")]
    public async Task<ActionResult<WorkflowSeedResponse>> SeedLifecycle(CancellationToken cancellationToken)
    {
        var result = await _workflowTransitionService.SeedLifecycleAsync(cancellationToken);

        return Ok(new WorkflowSeedResponse(
            result.CaseId,
            result.ClaimAId,
            result.ClaimBId,
            result.ContradictionId,
            result.CaseStatus,
            result.ContradictionStatus,
            DateTime.UtcNow));
    }
}