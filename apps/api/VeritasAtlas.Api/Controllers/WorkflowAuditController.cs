using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using VeritasAtlas.Api.Contracts.Workflow;
using VeritasAtlas.Infrastructure.Services;

namespace VeritasAtlas.Api.Controllers;

[ApiController]
[Authorize]
[Route("api/v1/workflow-audit")]
public class WorkflowAuditController : ControllerBase
{
    private readonly WorkflowAuditStore _workflowAuditStore;
    private readonly WorkflowIntegrityService _workflowIntegrityService;

    public WorkflowAuditController(
        WorkflowAuditStore workflowAuditStore,
        WorkflowIntegrityService workflowIntegrityService)
    {
        _workflowAuditStore = workflowAuditStore;
        _workflowIntegrityService = workflowIntegrityService;
    }

    [HttpGet("entries")]
    public async Task<ActionResult<IReadOnlyList<WorkflowAuditEntryResponse>>> GetEntries(CancellationToken cancellationToken)
    {
        var entries = (await _workflowAuditStore.GetAllAsync(cancellationToken))
            .Select(x => new WorkflowAuditEntryResponse(
                x.Id,
                x.EntityType,
                x.EntityId,
                x.ActionName,
                x.PreviousStatus,
                x.NextStatus,
                x.Role,
                x.Success,
                x.Message,
                x.TimestampUtc))
            .ToList();

        return Ok(entries);
    }

    [HttpPost("clear")]
    public async Task<IActionResult> Clear(CancellationToken cancellationToken)
    {
        await _workflowAuditStore.ClearAsync(cancellationToken);
        return Ok(new { Cleared = true, TimestampUtc = DateTime.UtcNow });
    }

    [HttpGet("rules")]
    public IActionResult GetRules()
    {
        return Ok(new
        {
            Transitions = _workflowIntegrityService.GetRules(),
            Roles = _workflowIntegrityService.GetRolePolicies(),
            TimestampUtc = DateTime.UtcNow
        });
    }
}