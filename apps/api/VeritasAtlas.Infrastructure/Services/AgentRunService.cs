using Microsoft.EntityFrameworkCore;
using VeritasAtlas.Application.Common;
using VeritasAtlas.Application.Interfaces;
using VeritasAtlas.Application.Models;
using VeritasAtlas.Domain.Entities;
using VeritasAtlas.Domain.Enums;
using VeritasAtlas.Infrastructure.Persistence;

namespace VeritasAtlas.Infrastructure.Services;

public sealed class AgentRunService : IAgentRunService
{
    private readonly VeritasAtlasDbContext _dbContext;

    public AgentRunService(VeritasAtlasDbContext dbContext)
    {
        _dbContext = dbContext;
    }

    public async Task<PagedResult<AgentRun>> GetAgentRunsAsync(AgentRunListFilters filters, CancellationToken cancellationToken = default)
    {
        var query = _dbContext.AgentRuns.AsNoTracking().AsQueryable();

        if (!string.IsNullOrWhiteSpace(filters.Status) &&
            Enum.TryParse<AgentRunStatus>(filters.Status, true, out var status))
        {
            query = query.Where(x => x.Status == status);
        }

        if (!string.IsNullOrWhiteSpace(filters.AgentType) &&
            Enum.TryParse<AgentType>(filters.AgentType, true, out var agentType))
        {
            query = query.Where(x => x.Type == agentType);
        }

        if (filters.CaseId.HasValue)
        {
            query = query.Where(x => x.CaseId == filters.CaseId.Value);
        }

        query = query.OrderByDescending(x => x.CreatedAtUtc);

        var totalCount = await query.CountAsync(cancellationToken);
        var items = await query
            .Skip((filters.Page - 1) * filters.PageSize)
            .Take(filters.PageSize)
            .ToListAsync(cancellationToken);

        return new PagedResult<AgentRun>
        {
            Items = items,
            Page = filters.Page,
            PageSize = filters.PageSize,
            TotalCount = totalCount,
            TotalPages = (int)Math.Ceiling(totalCount / (double)filters.PageSize)
        };
    }

    public async Task<AgentRun> StartAgentRunAsync(
        string agentName,
        AgentType agentType,
        Guid? caseId = null,
        string? inputPayload = null,
        string? createdBy = null,
        CancellationToken cancellationToken = default)
    {
        var entity = new AgentRun
        {
            CaseId = caseId,
            AgentName = agentName,
            Type = agentType,
            Status = AgentRunStatus.Running,
            StartedAtUtc = DateTime.UtcNow,
            InputHash = inputPayload?.GetHashCode().ToString()
        };

        _dbContext.AgentRuns.Add(entity);
        await _dbContext.SaveChangesAsync(cancellationToken);

        return entity;
    }

    public async Task CompleteAgentRunAsync(
        Guid agentRunId,
        string? outputPayload = null,
        string? errorMessage = null,
        string? notes = null,
        string? updatedBy = null,
        CancellationToken cancellationToken = default)
    {
        var entity = await _dbContext.AgentRuns.FirstOrDefaultAsync(x => x.Id == agentRunId, cancellationToken);
        if (entity is null)
        {
            throw new NotFoundException($"Agent run '{agentRunId}' was not found.");
        }

        entity.Status = AgentRunStatus.Succeeded;
        entity.CompletedAtUtc = DateTime.UtcNow;
        entity.OutputHash = outputPayload?.GetHashCode().ToString();
        entity.Error = errorMessage;
        entity.UpdatedAtUtc = DateTime.UtcNow;

        await _dbContext.SaveChangesAsync(cancellationToken);
    }

    public async Task FailAgentRunAsync(
        Guid agentRunId,
        string? errorMessage = null,
        string? notes = null,
        string? updatedBy = null,
        CancellationToken cancellationToken = default)
    {
        var entity = await _dbContext.AgentRuns.FirstOrDefaultAsync(x => x.Id == agentRunId, cancellationToken);
        if (entity is null)
        {
            throw new NotFoundException($"Agent run '{agentRunId}' was not found.");
        }

        entity.Status = AgentRunStatus.Failed;
        entity.CompletedAtUtc = DateTime.UtcNow;
        entity.Error = errorMessage;
        entity.UpdatedAtUtc = DateTime.UtcNow;

        await _dbContext.SaveChangesAsync(cancellationToken);
    }
}
