using VeritasAtlas.Application.Models;
using VeritasAtlas.Domain.Entities;
using VeritasAtlas.Domain.Enums;

namespace VeritasAtlas.Application.Interfaces;

public interface IAgentRunService
{
    Task<PagedResult<AgentRun>> GetAgentRunsAsync(AgentRunListFilters filters, CancellationToken cancellationToken = default);
    Task<AgentRun> StartAgentRunAsync(
        string agentName,
        AgentType agentType,
        Guid? caseId = null,
        string? inputPayload = null,
        string? createdBy = null,
        CancellationToken cancellationToken = default);
    Task CompleteAgentRunAsync(
        Guid agentRunId,
        string? outputPayload = null,
        string? errorMessage = null,
        string? notes = null,
        string? updatedBy = null,
        CancellationToken cancellationToken = default);
    Task FailAgentRunAsync(
        Guid agentRunId,
        string? errorMessage = null,
        string? notes = null,
        string? updatedBy = null,
        CancellationToken cancellationToken = default);
}
