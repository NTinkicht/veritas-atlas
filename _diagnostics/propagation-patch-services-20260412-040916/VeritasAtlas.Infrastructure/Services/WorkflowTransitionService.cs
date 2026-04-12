using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using VeritasAtlas.Infrastructure.Persistence;
using VeritasAtlas.Domain.Enums;
namespace VeritasAtlas.Infrastructure.Services;

public sealed class WorkflowTransitionService
{
    private readonly VeritasAtlasDbContext _dbContext;
    private readonly WorkflowIntegrityService _workflowIntegrityService;

    public WorkflowTransitionService(
        VeritasAtlasDbContext dbContext,
        WorkflowIntegrityService workflowIntegrityService)
    {
        _dbContext = dbContext;
        _workflowIntegrityService = workflowIntegrityService;
    }

    public async Task<WorkflowTransitionResult> SubmitCaseAsync(Guid caseId, CancellationToken cancellationToken = default)
    {
        return await TransitionCaseAsync(caseId, "SubmitCase", "InReview", "SubmitCase", cancellationToken);
    }

    public async Task<WorkflowTransitionResult> ApproveCaseAsync(Guid caseId, CancellationToken cancellationToken = default)
    {
        return await TransitionCaseAsync(caseId, "ApproveCase", "Approved", "ApproveCase", cancellationToken);
    }

    public async Task<WorkflowTransitionResult> RejectCaseAsync(Guid caseId, CancellationToken cancellationToken = default)
    {
        return await TransitionCaseAsync(caseId, "RejectCase", "Rejected", "RejectCase", cancellationToken);
    }

    public async Task<WorkflowTransitionResult> PreparePublicationAsync(Guid caseId, CancellationToken cancellationToken = default)
    {
        return await TransitionCaseAsync(caseId, "PreparePublication", "ReadyForPublication", "PreparePublication", cancellationToken);
    }

    public async Task<WorkflowTransitionResult> PublishCaseAsync(Guid caseId, CancellationToken cancellationToken = default)
    {
        return await TransitionCaseAsync(caseId, "PublishCase", "Published", "PublishCase", cancellationToken);
    }

    public async Task<WorkflowTransitionResult> HoldCaseAsync(Guid caseId, CancellationToken cancellationToken = default)
    {
        return await TransitionCaseAsync(caseId, "HoldCase", "OnHold", "HoldCase", cancellationToken);
    }

	private async Task<WorkflowTransitionResult> TransitionCaseAsync(
		Guid caseId,
		string policyAction,
		CaseStatus nextStatus,
		string auditAction,
		CancellationToken cancellationToken)
	{
		const string role = "admin";
	
		_workflowIntegrityService.EnsureRoleAllowed(policyAction, role);
	
		var entity = await _dbContext.Cases.FirstOrDefaultAsync(x => x.Id == caseId, cancellationToken);
		if (entity is null)
		{
			throw new InvalidOperationException($"Case '{caseId}' was not found.");
		}
	
		var previousStatus = entity.Status;
	
		_workflowIntegrityService.EnsureTransitionAllowed("Case", previousStatus.ToString(), nextStatus.ToString());
	
		entity.Status = nextStatus;
		entity.UpdatedAtUtc = DateTime.UtcNow;
	
		await _dbContext.SaveChangesAsync(cancellationToken);
	
		return new WorkflowTransitionResult(entity.Id, entity.Status);
	}

    private async Task UpsertScenarioSnapshotCaseStatusAsync(Guid caseId, string caseStatus, CancellationToken cancellationToken)
    {
        var store = await _dbContext.SystemStateEntries.FirstOrDefaultAsync(
            x => x.Key == "scenario.snapshot",
            cancellationToken);

        if (store is null || string.IsNullOrWhiteSpace(store.ValueJson))
        {
            return;
        }

        Dictionary<string, object?> snapshot;
        try
        {
            snapshot = JsonSerializer.Deserialize<Dictionary<string, object?>>(store.ValueJson) ??
                       new Dictionary<string, object?>(StringComparer.OrdinalIgnoreCase);
        }
        catch
        {
            snapshot = new Dictionary<string, object?>(StringComparer.OrdinalIgnoreCase);
        }

        var snapshotCaseId = snapshot.TryGetValue("caseId", out var caseIdValue) ? ConvertToString(caseIdValue) : "";
        if (!Guid.TryParse(snapshotCaseId, out var parsedCaseId) || parsedCaseId != caseId)
        {
            return;
        }

        snapshot["exists"] = true;
        snapshot["caseId"] = caseId.ToString();
        snapshot["caseStatus"] = caseStatus;
        snapshot["timestampUtc"] = DateTime.UtcNow;

        store.ValueJson = JsonSerializer.Serialize(snapshot);
        store.UpdatedAtUtc = DateTime.UtcNow;
    }

    private static string ConvertToString(object? value)
    {
        if (value is null)
        {
            return string.Empty;
        }

        if (value is JsonElement element)
        {
            return element.ValueKind switch
            {
                JsonValueKind.String => element.GetString() ?? string.Empty,
                JsonValueKind.Null => string.Empty,
                _ => element.ToString()
            };
        }

        return value.ToString() ?? string.Empty;
    }
}

public sealed record WorkflowTransitionResult(Guid Id, string Status);