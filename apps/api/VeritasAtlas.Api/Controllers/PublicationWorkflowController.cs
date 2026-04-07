using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using VeritasAtlas.Api.Contracts.Workflow;
using VeritasAtlas.Api.Infrastructure.Auth;
using VeritasAtlas.Infrastructure.Services;

namespace VeritasAtlas.Api.Controllers;

[ApiController]
[Authorize]
[Route("api/v1/publication-workflow")]
public class PublicationWorkflowController : ControllerBase
{
    private readonly WorkflowOrchestratorService _workflowOrchestratorService;
    private readonly AuthRequestContext _authRequestContext;

    public PublicationWorkflowController(
        WorkflowOrchestratorService workflowOrchestratorService,
        AuthRequestContext authRequestContext)
    {
        _workflowOrchestratorService = workflowOrchestratorService;
        _authRequestContext = authRequestContext;
    }

    [HttpPost("cases/{caseId}/prepare")]
    public async Task<ActionResult<WorkflowTransitionResponse>> PreparePublication(Guid caseId, CancellationToken cancellationToken)
    {
        return await ExecuteTransition(
            "Case",
            caseId,
            () => _workflowOrchestratorService.PreparePublicationAsync(caseId, _authRequestContext.GetRole(HttpContext), cancellationToken),
            "Case prepared for publication.");
    }

    [HttpPost("cases/{caseId}/publish")]
    public async Task<ActionResult<WorkflowTransitionResponse>> PublishCase(Guid caseId, CancellationToken cancellationToken)
    {
        return await ExecuteTransition(
            "Case",
            caseId,
            () => _workflowOrchestratorService.PublishCaseAsync(caseId, _authRequestContext.GetRole(HttpContext), cancellationToken),
            "Case published.");
    }

    [HttpPost("cases/{caseId}/hold")]
    public async Task<ActionResult<WorkflowTransitionResponse>> HoldCase(Guid caseId, CancellationToken cancellationToken)
    {
        return await ExecuteTransition(
            "Case",
            caseId,
            () => _workflowOrchestratorService.HoldCaseAsync(caseId, _authRequestContext.GetRole(HttpContext), cancellationToken),
            "Case put on hold.");
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