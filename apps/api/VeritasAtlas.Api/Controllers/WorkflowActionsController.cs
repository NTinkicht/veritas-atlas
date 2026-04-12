using System.Security.Claims;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using VeritasAtlas.Infrastructure.Services;

namespace VeritasAtlas.Api.Controllers;

[ApiController]
[Authorize]
[Route("api/v1/actions")]
public sealed class WorkflowActionsController : ControllerBase
{
    private readonly WorkflowOrchestratorService _workflowOrchestratorService;

    public WorkflowActionsController(WorkflowOrchestratorService workflowOrchestratorService)
    {
        _workflowOrchestratorService = workflowOrchestratorService;
    }

    [HttpPost("cases/{caseId:guid}/submit")]
    public async Task<IActionResult> SubmitCase(Guid caseId, CancellationToken cancellationToken)
    {
        var result = await _workflowOrchestratorService.SubmitCaseAsync(caseId, GetRole(), cancellationToken);
        return Ok(new { result.Id, result.Status, Message = "Case submitted successfully." });
    }

    [HttpPost("cases/{caseId:guid}/approve")]
    public async Task<IActionResult> ApproveCase(Guid caseId, CancellationToken cancellationToken)
    {
        var result = await _workflowOrchestratorService.ApproveCaseAsync(caseId, GetRole(), cancellationToken);
        return Ok(new { result.Id, result.Status, Message = "Case approved successfully." });
    }

    [HttpPost("cases/{caseId:guid}/reject")]
    public async Task<IActionResult> RejectCase(Guid caseId, CancellationToken cancellationToken)
    {
        var result = await _workflowOrchestratorService.RejectCaseAsync(caseId, GetRole(), cancellationToken);
        return Ok(new { result.Id, result.Status, Message = "Case rejected successfully." });
    }

    [HttpPost("cases/{caseId:guid}/prepare")]
    public async Task<IActionResult> PreparePublication(Guid caseId, CancellationToken cancellationToken)
    {
        var result = await _workflowOrchestratorService.PreparePublicationAsync(caseId, GetRole(), cancellationToken);
        return Ok(new { result.Id, result.Status, Message = "Case prepared for publication successfully." });
    }

    [HttpPost("cases/{caseId:guid}/publish")]
    public async Task<IActionResult> PublishCase(Guid caseId, CancellationToken cancellationToken)
    {
        var result = await _workflowOrchestratorService.PublishCaseAsync(caseId, GetRole(), cancellationToken);
        return Ok(new { result.Id, result.Status, Message = "Case published successfully." });
    }

    [HttpPost("cases/{caseId:guid}/hold")]
    public async Task<IActionResult> HoldCase(Guid caseId, CancellationToken cancellationToken)
    {
        var result = await _workflowOrchestratorService.HoldCaseAsync(caseId, GetRole(), cancellationToken);
        return Ok(new { result.Id, result.Status, Message = "Case placed on hold successfully." });
    }

    private string? GetRole()
    {
        return User.FindFirstValue(ClaimTypes.Role) ?? User.FindFirstValue("role");
    }
}