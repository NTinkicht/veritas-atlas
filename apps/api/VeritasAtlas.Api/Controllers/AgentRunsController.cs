using Microsoft.AspNetCore.Mvc;
using VeritasAtlas.Api.Contracts.AgentRuns;
using VeritasAtlas.Application.Interfaces;
using VeritasAtlas.Application.Models;
using VeritasAtlas.Domain.Enums;

namespace VeritasAtlas.Api.Controllers;

[ApiController]
[Route("api/v1/agent-runs")]
public sealed class AgentRunsController : ControllerBase
{
    private readonly IAgentRunService _agentRunService;

    public AgentRunsController(IAgentRunService agentRunService)
    {
        _agentRunService = agentRunService;
    }

    [HttpGet]
    public async Task<ActionResult<GetAgentRunsResponse>> GetAgentRuns(
        [FromQuery] int page = 1,
        [FromQuery] int pageSize = 20,
        [FromQuery] string? status = null,
        [FromQuery] string? agentType = null,
        [FromQuery] Guid? caseId = null,
        CancellationToken cancellationToken = default)
    {
        var result = await _agentRunService.GetAgentRunsAsync(
            new AgentRunListFilters
            {
                Page = page,
                PageSize = pageSize,
                Status = status,
                AgentType = agentType,
                CaseId = caseId
            },
            cancellationToken);

        return Ok(new GetAgentRunsResponse(
            result.Items.Select(MapItem).ToArray(),
            result.Page,
            result.PageSize,
            result.TotalCount,
            result.TotalPages));
    }

    [HttpPost("start")]
    public async Task<ActionResult<StartAgentRunResponse>> Start(
        [FromBody] StartAgentRunRequest request,
        CancellationToken cancellationToken = default)
    {
        if (!Enum.TryParse<AgentType>(request.AgentType, true, out var agentType))
        {
            return BadRequest(new { message = "Invalid agentType." });
        }

        var entity = await _agentRunService.StartAgentRunAsync(
            request.AgentName,
            agentType,
            request.CaseId,
            request.InputPayload,
            request.CreatedBy,
            cancellationToken);

        return Ok(new StartAgentRunResponse(
            entity.Id,
            entity.CaseId,
            entity.AgentName,
            entity.Type.ToString(),
            entity.Status.ToString(),
            entity.StartedAtUtc,
            entity.CreatedAtUtc));
    }

    [HttpPost("{id:guid}/complete")]
    public async Task<IActionResult> Complete(
        Guid id,
        [FromBody] CompleteAgentRunRequest request,
        CancellationToken cancellationToken = default)
    {
        await _agentRunService.CompleteAgentRunAsync(
            id,
            request.OutputPayload,
            request.ErrorMessage,
            request.Notes,
            request.UpdatedBy,
            cancellationToken);

        return NoContent();
    }

    [HttpPost("{id:guid}/fail")]
    public async Task<IActionResult> Fail(
        Guid id,
        [FromBody] FailAgentRunRequest request,
        CancellationToken cancellationToken = default)
    {
        await _agentRunService.FailAgentRunAsync(
            id,
            request.ErrorMessage,
            request.Notes,
            request.UpdatedBy,
            cancellationToken);

        return NoContent();
    }

    private static GetAgentRunsItemResponse MapItem(VeritasAtlas.Domain.Entities.AgentRun entity)
    {
        return new GetAgentRunsItemResponse(
            entity.Id,
            entity.CaseId,
            entity.AgentName,
            entity.Type.ToString(),
            entity.Status.ToString(),
            entity.StartedAtUtc,
            entity.CompletedAtUtc,
            entity.CreatedAtUtc);
    }
}
