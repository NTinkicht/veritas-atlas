using Microsoft.AspNetCore.Mvc;
using VeritasAtlas.Api.Contracts.Workflow;
using VeritasAtlas.Infrastructure.Services;

namespace VeritasAtlas.Api.Controllers;

[ApiController]
[Route("api/v1/actions")]
public class ActionsController : ControllerBase
{
    private readonly WorkflowOrchestratorService _workflowOrchestratorService;

    public ActionsController(WorkflowOrchestratorService workflowOrchestratorService)
    {
        _workflowOrchestratorService = workflowOrchestratorService;
    }

    [HttpPost("cases/{caseId}/submit")]
    public async Task<ActionResult<WorkflowTransitionResponse>> SubmitCase(Guid caseId, CancellationToken cancellationToken)
    {
        return await ExecuteTransition(
            "Case",
            caseId,
            () => _workflowOrchestratorService.SubmitCaseAsync(caseId, Request.Headers["X-Role"], cancellationToken),
            "Case submitted.");
    }

    [HttpPost("cases/{caseId}/approve")]
    public async Task<ActionResult<WorkflowTransitionResponse>> ApproveCase(Guid caseId, CancellationToken cancellationToken)
    {
        return await ExecuteTransition(
            "Case",
            caseId,
            () => _workflowOrchestratorService.ApproveCaseAsync(caseId, Request.Headers["X-Role"], cancellationToken),
            "Case approved.");
    }

    [HttpPost("cases/{caseId}/reject")]
    public async Task<ActionResult<WorkflowTransitionResponse>> RejectCase(Guid caseId, CancellationToken cancellationToken)
    {
        return await ExecuteTransition(
            "Case",
            caseId,
            () => _workflowOrchestratorService.RejectCaseAsync(caseId, Request.Headers["X-Role"], cancellationToken),
            "Case rejected.");
    }

    [HttpPost("contradictions/{id}/resolve")]
    public async Task<ActionResult<WorkflowTransitionResponse>> ResolveContradiction(Guid id, CancellationToken cancellationToken)
    {
        return await ExecuteTransition(
            "Contradiction",
            id,
            () => _workflowOrchestratorService.ResolveContradictionAsync(id, Request.Headers["X-Role"], cancellationToken),
            "Contradiction resolved.");
    }

    [HttpPost("reviews/{id}/complete")]
    public async Task<ActionResult<WorkflowTransitionResponse>> CompleteReview(Guid id, CancellationToken cancellationToken)
    {
        return await ExecuteTransition(
            "Review",
            id,
            () => _workflowOrchestratorService.CompleteReviewAsync(id, Request.Headers["X-Role"], cancellationToken),
            "Review completed.");
    }

    [HttpPost("seed/lifecycle")]
    public async Task<ActionResult<WorkflowSeedResponse>> SeedLifecycle(CancellationToken cancellationToken)
    {
        try
        {
            var result = await _workflowOrchestratorService.SeedLifecycleAsync(Request.Headers["X-Role"], cancellationToken);

            return Ok(new WorkflowSeedResponse(
                result.CaseId,
                result.ClaimAId,
                result.ClaimBId,
                result.ContradictionId,
                result.CaseStatus,
                result.ContradictionStatus,
                DateTime.UtcNow));
        }
        catch (UnauthorizedAccessException ex)
        {
            return StatusCode(StatusCodes.Status403Forbidden, ex.Message);
        }
        catch (InvalidOperationException ex)
        {
            return BadRequest(ex.Message);
        }
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