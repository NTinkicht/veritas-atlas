using System.Text.Json;

namespace VeritasAtlas.Infrastructure.Services;

public sealed class PersistentWorkflowAuditService
{
    private readonly string _auditPath;
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web) { WriteIndented = true };

    public PersistentWorkflowAuditService()
    {
        var root = AppContext.BaseDirectory;
        _auditPath = Path.Combine(root, "workflow-audit-store.json");
    }

    public async Task AppendAsync(WorkflowAuditRecord record, CancellationToken cancellationToken = default)
    {
        var records = await ReadAllAsync(cancellationToken);
        records.Add(record);

        await using var stream = File.Create(_auditPath);
        await JsonSerializer.SerializeAsync(stream, records, JsonOptions, cancellationToken);
    }

    public async Task<IReadOnlyList<WorkflowAuditRecord>> GetAllAsync(CancellationToken cancellationToken = default)
    {
        var records = await ReadAllAsync(cancellationToken);
        return records.OrderByDescending(x => x.TimestampUtc).ToList();
    }

    public async Task ClearAsync(CancellationToken cancellationToken = default)
    {
        await using var stream = File.Create(_auditPath);
        await JsonSerializer.SerializeAsync(stream, new List<WorkflowAuditRecord>(), JsonOptions, cancellationToken);
    }

    private async Task<List<WorkflowAuditRecord>> ReadAllAsync(CancellationToken cancellationToken)
    {
        if (!File.Exists(_auditPath))
        {
            return new List<WorkflowAuditRecord>();
        }

        await using var stream = File.OpenRead(_auditPath);
        var result = await JsonSerializer.DeserializeAsync<List<WorkflowAuditRecord>>(stream, JsonOptions, cancellationToken);
        return result ?? new List<WorkflowAuditRecord>();
    }
}