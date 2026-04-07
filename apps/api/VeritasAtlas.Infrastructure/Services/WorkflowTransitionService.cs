using Microsoft.EntityFrameworkCore;
using VeritasAtlas.Domain.Entities;
using VeritasAtlas.Domain.Enums;
using VeritasAtlas.Infrastructure.Persistence;

namespace VeritasAtlas.Infrastructure.Services;

public sealed class WorkflowTransitionService
{
    private readonly VeritasAtlasDbContext _dbContext;
    private readonly ScenarioPersistenceService _scenarioPersistenceService;

    public WorkflowTransitionService(
        VeritasAtlasDbContext dbContext,
        ScenarioPersistenceService scenarioPersistenceService)
    {
        _dbContext = dbContext;
        _scenarioPersistenceService = scenarioPersistenceService;
    }

    public async Task<(Guid Id, string Status)> SubmitCaseAsync(Guid caseId, CancellationToken cancellationToken = default)
    {
        var entity = await GetCaseAsync(caseId, cancellationToken);
        entity.Status = ParseCaseStatus("InReview", entity.Status);
        Touch(entity);
        await _dbContext.SaveChangesAsync(cancellationToken);
        return (entity.Id, entity.Status.ToString());
    }

    public async Task<(Guid Id, string Status)> ApproveCaseAsync(Guid caseId, CancellationToken cancellationToken = default)
    {
        var entity = await GetCaseAsync(caseId, cancellationToken);
        entity.Status = ParseCaseStatus("Approved", entity.Status);
        Touch(entity);
        await _dbContext.SaveChangesAsync(cancellationToken);
        return (entity.Id, entity.Status.ToString());
    }

    public async Task<(Guid Id, string Status)> RejectCaseAsync(Guid caseId, CancellationToken cancellationToken = default)
    {
        var entity = await GetCaseAsync(caseId, cancellationToken);
        entity.Status = ParseCaseStatus("Rejected", entity.Status);
        Touch(entity);
        await _dbContext.SaveChangesAsync(cancellationToken);
        return (entity.Id, entity.Status.ToString());
    }

    public async Task<(Guid Id, string Status)> PreparePublicationAsync(Guid caseId, CancellationToken cancellationToken = default)
    {
        var entity = await GetCaseAsync(caseId, cancellationToken);
        entity.Status = ParseCaseStatus("ReadyForPublication", entity.Status);
        Touch(entity);
        await _dbContext.SaveChangesAsync(cancellationToken);
        return (entity.Id, entity.Status.ToString());
    }

    public async Task<(Guid Id, string Status)> PublishCaseAsync(Guid caseId, CancellationToken cancellationToken = default)
    {
        var entity = await GetCaseAsync(caseId, cancellationToken);
        entity.Status = ParseCaseStatus("Published", entity.Status);
        Touch(entity);
        await _dbContext.SaveChangesAsync(cancellationToken);
        return (entity.Id, entity.Status.ToString());
    }

    public async Task<(Guid Id, string Status)> HoldCaseAsync(Guid caseId, CancellationToken cancellationToken = default)
    {
        var entity = await GetCaseAsync(caseId, cancellationToken);
        entity.Status = ParseCaseStatus("OnHold", entity.Status);
        Touch(entity);
        await _dbContext.SaveChangesAsync(cancellationToken);
        return (entity.Id, entity.Status.ToString());
    }

    public async Task<(Guid Id, string Status)> ResolveContradictionAsync(Guid contradictionId, CancellationToken cancellationToken = default)
    {
        var entity = await GetContradictionAsync(contradictionId, cancellationToken);
        entity.Status = ContradictionStatus.Resolved;
        Touch(entity);
        await _dbContext.SaveChangesAsync(cancellationToken);
        await UpdateSnapshotAsync(cancellationToken);
        return (entity.Id, entity.Status.ToString());
    }

    public async Task<(Guid Id, string Status)> EscalateContradictionAsync(Guid contradictionId, CancellationToken cancellationToken = default)
    {
        var entity = await GetContradictionAsync(contradictionId, cancellationToken);
        entity.Status = ParseContradictionStatus("UnderReview", entity.Status);
        Touch(entity);
        await _dbContext.SaveChangesAsync(cancellationToken);
        await UpdateSnapshotAsync(cancellationToken);
        return (entity.Id, entity.Status.ToString());
    }

    public async Task<(Guid Id, string Status)> SendClaimToReviewAsync(Guid claimId, CancellationToken cancellationToken = default)
    {
        var entity = await GetClaimAsync(claimId, cancellationToken);
        entity.Status = ParseClaimStatus("InReview", entity.Status);
        Touch(entity);
        await _dbContext.SaveChangesAsync(cancellationToken);
        return (entity.Id, entity.Status.ToString());
    }

    public async Task<(Guid Id, string Status)> ReturnClaimForEditAsync(Guid claimId, CancellationToken cancellationToken = default)
    {
        var entity = await GetClaimAsync(claimId, cancellationToken);
        entity.Status = ParseClaimStatus("Draft", entity.Status);
        Touch(entity);
        await _dbContext.SaveChangesAsync(cancellationToken);
        return (entity.Id, entity.Status.ToString());
    }

    public async Task<(Guid Id, string Status)> CompleteReviewAsync(Guid reviewId, CancellationToken cancellationToken = default)
    {
        var entity = await GetReviewAsync(reviewId, cancellationToken);
        entity.Status = ParseReviewStatus("Completed", entity.Status);
        Touch(entity);
        await _dbContext.SaveChangesAsync(cancellationToken);
        return (entity.Id, entity.Status.ToString());
    }

    public async Task<(Guid Id, string Status)> ReopenReviewAsync(Guid reviewId, CancellationToken cancellationToken = default)
    {
        var entity = await GetReviewAsync(reviewId, cancellationToken);
        entity.Status = ParseReviewStatus("Open", entity.Status);
        Touch(entity);
        await _dbContext.SaveChangesAsync(cancellationToken);
        return (entity.Id, entity.Status.ToString());
    }

    public async Task<(Guid CaseId, Guid ClaimAId, Guid ClaimBId, Guid ContradictionId, string CaseStatus, string ContradictionStatus)> SeedLifecycleAsync(CancellationToken cancellationToken = default)
    {
        var caseStatus = ParseCaseStatus("Draft", default);
        var claimStatus = ParseClaimStatus("Draft", default);

        var @case = new Case
        {
            Title = "Phase 12 Seed Case",
            Status = caseStatus
        };
        Touch(@case);

        _dbContext.Cases.Add(@case);
        await _dbContext.SaveChangesAsync(cancellationToken);

        var claimA = new Claim
        {
            CaseId = @case.Id,
            Topic = "Seed Claim A",
            NormalizedText = "Seed claim A normalized text",
            Status = claimStatus
        };
        Touch(claimA);

        var claimB = new Claim
        {
            CaseId = @case.Id,
            Topic = "Seed Claim B",
            NormalizedText = "Seed claim B normalized text",
            Status = claimStatus
        };
        Touch(claimB);

        _dbContext.Claims.Add(claimA);
        _dbContext.Claims.Add(claimB);
        await _dbContext.SaveChangesAsync(cancellationToken);

        var contradiction = new Contradiction
        {
            CaseId = @case.Id,
            LeftClaimId = claimA.Id,
            RightClaimId = claimB.Id,
            Type = ContradictionType.Direct,
            Severity = ContradictionSeverity.Medium,
            Status = ContradictionStatus.Draft,
            Summary = "Seed contradiction"
        };
        Touch(contradiction);

        _dbContext.Contradictions.Add(contradiction);
        await _dbContext.SaveChangesAsync(cancellationToken);

        await _scenarioPersistenceService.SaveSnapshotAsync(
            new ScenarioSnapshot(
                @case.Id,
                claimA.Id,
                claimB.Id,
                contradiction.Id,
                DateTime.UtcNow,
                @case.Status.ToString(),
                contradiction.Status.ToString()),
            cancellationToken);

        return (@case.Id, claimA.Id, claimB.Id, contradiction.Id, @case.Status.ToString(), contradiction.Status.ToString());
    }

    public async Task UpdateSnapshotAsync(CancellationToken cancellationToken = default)
    {
        var snapshot = await _scenarioPersistenceService.LoadSnapshotAsync(cancellationToken);
        if (snapshot is null)
        {
            return;
        }

        var @case = await _dbContext.Cases.FirstOrDefaultAsync(x => x.Id == snapshot.CaseId, cancellationToken);
        var contradiction = await _dbContext.Contradictions.FirstOrDefaultAsync(x => x.Id == snapshot.ContradictionId, cancellationToken);

        if (@case is null || contradiction is null)
        {
            return;
        }

        await _scenarioPersistenceService.SaveSnapshotAsync(
            snapshot with
            {
                CaseStatus = @case.Status.ToString(),
                ContradictionStatus = contradiction.Status.ToString()
            },
            cancellationToken);
    }

    private async Task<Case> GetCaseAsync(Guid caseId, CancellationToken cancellationToken)
    {
        return await _dbContext.Cases.FirstOrDefaultAsync(x => x.Id == caseId, cancellationToken)
            ?? throw new InvalidOperationException($"Case '{caseId}' was not found.");
    }

    private async Task<Claim> GetClaimAsync(Guid claimId, CancellationToken cancellationToken)
    {
        return await _dbContext.Claims.FirstOrDefaultAsync(x => x.Id == claimId, cancellationToken)
            ?? throw new InvalidOperationException($"Claim '{claimId}' was not found.");
    }

    private async Task<Contradiction> GetContradictionAsync(Guid contradictionId, CancellationToken cancellationToken)
    {
        return await _dbContext.Contradictions.FirstOrDefaultAsync(x => x.Id == contradictionId, cancellationToken)
            ?? throw new InvalidOperationException($"Contradiction '{contradictionId}' was not found.");
    }

    private async Task<Review> GetReviewAsync(Guid reviewId, CancellationToken cancellationToken)
    {
        return await _dbContext.Reviews.FirstOrDefaultAsync(x => x.Id == reviewId, cancellationToken)
            ?? throw new InvalidOperationException($"Review '{reviewId}' was not found.");
    }

    private static void Touch(object entity)
    {
        var updatedAt = entity.GetType().GetProperty("UpdatedAtUtc");
        if (updatedAt is not null && updatedAt.CanWrite)
        {
            updatedAt.SetValue(entity, DateTime.UtcNow);
        }
    }

    private static TEnum ParseEnum<TEnum>(string desired, TEnum fallback) where TEnum : struct, Enum
    {
        if (Enum.TryParse<TEnum>(desired, true, out var parsed))
        {
            return parsed;
        }

        return fallback;
    }

    private static dynamic ParseCaseStatus(string desired, dynamic fallback) => ParseEnum(desired, fallback);
    private static dynamic ParseClaimStatus(string desired, dynamic fallback) => ParseEnum(desired, fallback);
    private static dynamic ParseReviewStatus(string desired, dynamic fallback) => ParseEnum(desired, fallback);
    private static ContradictionStatus ParseContradictionStatus(string desired, ContradictionStatus fallback) => ParseEnum(desired, fallback);
}