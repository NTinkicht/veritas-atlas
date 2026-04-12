namespace VeritasAtlas.Infrastructure.Services;

public sealed class WorkflowAuditStore
{
    private readonly PersistentWorkflowAuditService _persistentWorkflowAuditService;

    public WorkflowAuditStore(PersistentWorkflowAuditService persistentWorkflowAuditService)
    {
        _persistentWorkflowAuditService = persistentWorkflowAuditService;
    }

    public async Task AddAsync(WorkflowAuditRecord record, CancellationToken cancellationToken = default)
    {
        await _persistentWorkflowAuditService.AppendAsync(record, cancellationToken);
    }

    public async Task<IReadOnlyList<WorkflowAuditRecord>> GetAllAsync(CancellationToken cancellationToken = default)
    {
        return await _persistentWorkflowAuditService.GetAllAsync(cancellationToken);
    }

    public async Task ClearAsync(CancellationToken cancellationToken = default)
    {
        await _persistentWorkflowAuditService.ClearAsync(cancellationToken);
    }
}

public sealed record WorkflowAuditRecord(
    Guid Id,
    string EntityType,
    Guid EntityId,
    string ActionName,
    string? PreviousStatus,
    string NextStatus,
    string Role,
    bool Success,
    string Message,
    DateTime TimestampUtc);