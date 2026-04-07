using Microsoft.EntityFrameworkCore;
using VeritasAtlas.Domain.Entities;
using VeritasAtlas.Infrastructure.Persistence;

namespace VeritasAtlas.Infrastructure.Services;

public sealed class WorkflowOrchestratorService
{
    private readonly VeritasAtlasDbContext _dbContext;
    private readonly WorkflowTransitionService _workflowTransitionService;
    private readonly WorkflowIntegrityService _workflowIntegrityService;
    private readonly WorkflowAuditStore _workflowAuditStore;

    public WorkflowOrchestratorService(
        VeritasAtlasDbContext dbContext,
        WorkflowTransitionService workflowTransitionService,
        WorkflowIntegrityService workflowIntegrityService,
        WorkflowAuditStore workflowAuditStore)
    {
        _dbContext = dbContext;
        _workflowTransitionService = workflowTransitionService;
        _workflowIntegrityService = workflowIntegrityService;
        _workflowAuditStore = workflowAuditStore;
    }

    public async Task<(Guid Id, string Status)> SubmitCaseAsync(Guid caseId, string? role, CancellationToken cancellationToken = default)
    {
        return await RunCaseActionAsync("SubmitCase", caseId, "InReview", role, _workflowTransitionService.SubmitCaseAsync, cancellationToken);
    }

    public async Task<(Guid Id, string Status)> ApproveCaseAsync(Guid caseId, string? role, CancellationToken cancellationToken = default)
    {
        return await RunCaseActionAsync("ApproveCase", caseId, "Approved", role, _workflowTransitionService.ApproveCaseAsync, cancellationToken);
    }

    public async Task<(Guid Id, string Status)> RejectCaseAsync(Guid caseId, string? role, CancellationToken cancellationToken = default)
    {
        return await RunCaseActionAsync("RejectCase", caseId, "Rejected", role, _workflowTransitionService.RejectCaseAsync, cancellationToken);
    }

    public async Task<(Guid Id, string Status)> PreparePublicationAsync(Guid caseId, string? role, CancellationToken cancellationToken = default)
    {
        return await RunCaseActionAsync("PreparePublication", caseId, "ReadyForPublication", role, _workflowTransitionService.PreparePublicationAsync, cancellationToken);
    }

    public async Task<(Guid Id, string Status)> PublishCaseAsync(Guid caseId, string? role, CancellationToken cancellationToken = default)
    {
        return await RunCaseActionAsync("PublishCase", caseId, "Published", role, _workflowTransitionService.PublishCaseAsync, cancellationToken);
    }

    public async Task<(Guid Id, string Status)> HoldCaseAsync(Guid caseId, string? role, CancellationToken cancellationToken = default)
    {
        return await RunCaseActionAsync("HoldCase", caseId, "OnHold", role, _workflowTransitionService.HoldCaseAsync, cancellationToken);
    }

    public async Task<(Guid Id, string Status)> ResolveContradictionAsync(Guid contradictionId, string? role, CancellationToken cancellationToken = default)
    {
        return await RunContradictionActionAsync("ResolveContradiction", contradictionId, "Resolved", role, _workflowTransitionService.ResolveContradictionAsync, cancellationToken);
    }

    public async Task<(Guid Id, string Status)> EscalateContradictionAsync(Guid contradictionId, string? role, CancellationToken cancellationToken = default)
    {
        return await RunContradictionActionAsync("EscalateContradiction", contradictionId, "UnderReview", role, _workflowTransitionService.EscalateContradictionAsync, cancellationToken);
    }

    public async Task<(Guid Id, string Status)> SendClaimToReviewAsync(Guid claimId, string? role, CancellationToken cancellationToken = default)
    {
        return await RunClaimActionAsync("SendClaimToReview", claimId, "InReview", role, _workflowTransitionService.SendClaimToReviewAsync, cancellationToken);
    }

    public async Task<(Guid Id, string Status)> ReturnClaimForEditAsync(Guid claimId, string? role, CancellationToken cancellationToken = default)
    {
        return await RunClaimActionAsync("ReturnClaimForEdit", claimId, "Draft", role, _workflowTransitionService.ReturnClaimForEditAsync, cancellationToken);
    }

    public async Task<(Guid Id, string Status)> CompleteReviewAsync(Guid reviewId, string? role, CancellationToken cancellationToken = default)
    {
        return await RunReviewActionAsync("CompleteReview", reviewId, "Completed", role, _workflowTransitionService.CompleteReviewAsync, cancellationToken);
    }

    public async Task<(Guid Id, string Status)> ReopenReviewAsync(Guid reviewId, string? role, CancellationToken cancellationToken = default)
    {
        return await RunReviewActionAsync("ReopenReview", reviewId, "Open", role, _workflowTransitionService.ReopenReviewAsync, cancellationToken);
    }

    public async Task<(Guid CaseId, Guid ClaimAId, Guid ClaimBId, Guid ContradictionId, string CaseStatus, string ContradictionStatus)> SeedLifecycleAsync(string? role, CancellationToken cancellationToken = default)
    {
        _workflowIntegrityService.EnsureRoleAllowed("SeedLifecycle", role);

        var seeded = await _workflowTransitionService.SeedLifecycleAsync(cancellationToken);

        _workflowAuditStore.Add(new WorkflowAuditRecord(
            Guid.NewGuid(),
            "Seed",
            seeded.CaseId,
            "SeedLifecycle",
            null,
            seeded.CaseStatus,
            NormalizeRole(role),
            true,
            "Seeded lifecycle scenario.",
            DateTime.UtcNow));

        return seeded;
    }

    private async Task<(Guid Id, string Status)> RunCaseActionAsync(
        string actionName,
        Guid caseId,
        string nextStatus,
        string? role,
        Func<Guid, CancellationToken, Task<(Guid Id, string Status)>> operation,
        CancellationToken cancellationToken)
    {
        var entity = await _dbContext.Cases.FirstOrDefaultAsync(x => x.Id == caseId, cancellationToken)
            ?? throw new InvalidOperationException($"Case '{caseId}' was not found.");

        var previous = entity.Status.ToString();
        _workflowIntegrityService.EnsureRoleAllowed(actionName, role);
        _workflowIntegrityService.EnsureTransitionAllowed("Case", previous, nextStatus);

        try
        {
            var result = await operation(caseId, cancellationToken);
            _workflowAuditStore.Add(new WorkflowAuditRecord(Guid.NewGuid(), "Case", caseId, actionName, previous, result.Status, NormalizeRole(role), true, "Transition succeeded.", DateTime.UtcNow));
            return result;
        }
        catch (Exception ex)
        {
            _workflowAuditStore.Add(new WorkflowAuditRecord(Guid.NewGuid(), "Case", caseId, actionName, previous, nextStatus, NormalizeRole(role), false, ex.Message, DateTime.UtcNow));
            throw;
        }
    }

    private async Task<(Guid Id, string Status)> RunClaimActionAsync(
        string actionName,
        Guid claimId,
        string nextStatus,
        string? role,
        Func<Guid, CancellationToken, Task<(Guid Id, string Status)>> operation,
        CancellationToken cancellationToken)
    {
        var entity = await _dbContext.Claims.FirstOrDefaultAsync(x => x.Id == claimId, cancellationToken)
            ?? throw new InvalidOperationException($"Claim '{claimId}' was not found.");

        var previous = entity.Status.ToString();
        _workflowIntegrityService.EnsureRoleAllowed(actionName, role);
        _workflowIntegrityService.EnsureTransitionAllowed("Claim", previous, nextStatus);

        try
        {
            var result = await operation(claimId, cancellationToken);
            _workflowAuditStore.Add(new WorkflowAuditRecord(Guid.NewGuid(), "Claim", claimId, actionName, previous, result.Status, NormalizeRole(role), true, "Transition succeeded.", DateTime.UtcNow));
            return result;
        }
        catch (Exception ex)
        {
            _workflowAuditStore.Add(new WorkflowAuditRecord(Guid.NewGuid(), "Claim", claimId, actionName, previous, nextStatus, NormalizeRole(role), false, ex.Message, DateTime.UtcNow));
            throw;
        }
    }

    private async Task<(Guid Id, string Status)> RunContradictionActionAsync(
        string actionName,
        Guid contradictionId,
        string nextStatus,
        string? role,
        Func<Guid, CancellationToken, Task<(Guid Id, string Status)>> operation,
        CancellationToken cancellationToken)
    {
        var entity = await _dbContext.Contradictions.FirstOrDefaultAsync(x => x.Id == contradictionId, cancellationToken)
            ?? throw new InvalidOperationException($"Contradiction '{contradictionId}' was not found.");

        var previous = entity.Status.ToString();
        _workflowIntegrityService.EnsureRoleAllowed(actionName, role);
        _workflowIntegrityService.EnsureTransitionAllowed("Contradiction", previous, nextStatus);

        try
        {
            var result = await operation(contradictionId, cancellationToken);
            _workflowAuditStore.Add(new WorkflowAuditRecord(Guid.NewGuid(), "Contradiction", contradictionId, actionName, previous, result.Status, NormalizeRole(role), true, "Transition succeeded.", DateTime.UtcNow));
            return result;
        }
        catch (Exception ex)
        {
            _workflowAuditStore.Add(new WorkflowAuditRecord(Guid.NewGuid(), "Contradiction", contradictionId, actionName, previous, nextStatus, NormalizeRole(role), false, ex.Message, DateTime.UtcNow));
            throw;
        }
    }

    private async Task<(Guid Id, string Status)> RunReviewActionAsync(
        string actionName,
        Guid reviewId,
        string nextStatus,
        string? role,
        Func<Guid, CancellationToken, Task<(Guid Id, string Status)>> operation,
        CancellationToken cancellationToken)
    {
        var entity = await _dbContext.Reviews.FirstOrDefaultAsync(x => x.Id == reviewId, cancellationToken)
            ?? throw new InvalidOperationException($"Review '{reviewId}' was not found.");

        var previous = entity.Status.ToString();
        _workflowIntegrityService.EnsureRoleAllowed(actionName, role);
        _workflowIntegrityService.EnsureTransitionAllowed("Review", previous, nextStatus);

        try
        {
            var result = await operation(reviewId, cancellationToken);
            _workflowAuditStore.Add(new WorkflowAuditRecord(Guid.NewGuid(), "Review", reviewId, actionName, previous, result.Status, NormalizeRole(role), true, "Transition succeeded.", DateTime.UtcNow));
            return result;
        }
        catch (Exception ex)
        {
            _workflowAuditStore.Add(new WorkflowAuditRecord(Guid.NewGuid(), "Review", reviewId, actionName, previous, nextStatus, NormalizeRole(role), false, ex.Message, DateTime.UtcNow));
            throw;
        }
    }

    private static string NormalizeRole(string? role)
    {
        return string.IsNullOrWhiteSpace(role) ? "anonymous" : role.Trim().ToLowerInvariant();
    }
}