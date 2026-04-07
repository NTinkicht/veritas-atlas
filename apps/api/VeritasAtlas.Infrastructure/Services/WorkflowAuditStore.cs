using System.Collections.Concurrent;

namespace VeritasAtlas.Infrastructure.Services;

public sealed class WorkflowAuditStore
{
    private static readonly ConcurrentQueue<WorkflowAuditRecord> _records = new();

    public void Add(WorkflowAuditRecord record)
    {
        _records.Enqueue(record);
    }

    public IReadOnlyList<WorkflowAuditRecord> GetAll()
    {
        return _records.ToArray()
            .OrderByDescending(x => x.TimestampUtc)
            .ToList();
    }

    public void Clear()
    {
        while (_records.TryDequeue(out _)) { }
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