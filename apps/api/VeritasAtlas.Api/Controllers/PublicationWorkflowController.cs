using Microsoft.AspNetCore.Mvc;
using VeritasAtlas.Api.Contracts.Workflow;
using VeritasAtlas.Infrastructure.Services;

namespace VeritasAtlas.Api.Controllers;

[ApiController]
[Route("api/v1/publication-workflow")]
public class PublicationWorkflowController : ControllerBase
{
    private readonly WorkflowTransitionService _workflowTransitionService;

    public PublicationWorkflowController(WorkflowTransitionService workflowTransitionService)
    {
        _workflowTransitionService = workflowTransitionService;
    }

    [HttpPost("cases/{caseId}/prepare")]
    public async Task<ActionResult<WorkflowTransitionResponse>> PreparePublication(Guid caseId, CancellationToken cancellationToken)
    {
        var result = await _workflowTransitionService.PreparePublicationAsync(caseId, cancellationToken);
        return Ok(new WorkflowTransitionResponse("Case", result.Id, result.Status, DateTime.UtcNow, "Case prepared for publication."));
    }

    [HttpPost("cases/{caseId}/publish")]
    public async Task<ActionResult<WorkflowTransitionResponse>> PublishCase(Guid caseId, CancellationToken cancellationToken)
    {
        var result = await _workflowTransitionService.PublishCaseAsync(caseId, cancellationToken);
        return Ok(new WorkflowTransitionResponse("Case", result.Id, result.Status, DateTime.UtcNow, "Case published."));
    }

    [HttpPost("cases/{caseId}/hold")]
    public async Task<ActionResult<WorkflowTransitionResponse>> HoldCase(Guid caseId, CancellationToken cancellationToken)
    {
        var result = await _workflowTransitionService.HoldCaseAsync(caseId, cancellationToken);
        return Ok(new WorkflowTransitionResponse("Case", result.Id, result.Status, DateTime.UtcNow, "Case put on hold."));
    }
}