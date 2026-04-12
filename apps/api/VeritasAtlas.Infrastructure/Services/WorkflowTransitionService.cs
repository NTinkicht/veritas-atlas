using Microsoft.EntityFrameworkCore;
using VeritasAtlas.Domain.Entities;
using VeritasAtlas.Domain.Enums;
using VeritasAtlas.Infrastructure.Persistence;

namespace VeritasAtlas.Infrastructure.Services;

public class WorkflowTransitionService
{
    private readonly VeritasAtlasDbContext _db;
    private readonly ScenarioPersistenceService _scenarioPersistenceService;

    public WorkflowTransitionService(
        VeritasAtlasDbContext db,
        ScenarioPersistenceService scenarioPersistenceService)
    {
        _db = db;
        _scenarioPersistenceService = scenarioPersistenceService;
    }

    public async Task<(Guid CaseId, Guid ClaimAId, Guid ClaimBId, Guid ContradictionId, string CaseStatus, string ContradictionStatus)>
        SeedLifecycleAsync(CancellationToken ct)
    {
        var claims = await _db.Claims.Take(2).ToListAsync(ct);
        if (claims.Count < 2) throw new Exception("Not enough claims");

        var contradiction = await _db.Contradictions.FirstOrDefaultAsync(ct)
            ?? throw new Exception("No contradiction found");

        var caseEntity = await _db.Cases.FirstOrDefaultAsync(ct)
            ?? throw new Exception("No case found");

        var snapshot = new ScenarioSnapshot(
            caseEntity.Id,
            claims[0].Id,
            claims[1].Id,
            contradiction.Id,
            DateTime.UtcNow,
            caseEntity.Status.ToString(),
            contradiction.Status.ToString());

        await _scenarioPersistenceService.SaveSnapshotAsync(snapshot, ct);

        return (
            caseEntity.Id,
            claims[0].Id,
            claims[1].Id,
            contradiction.Id,
            caseEntity.Status.ToString(),
            contradiction.Status.ToString()
        );
    }

    public async Task<(Guid Id, string Status)> SubmitCaseAsync(Guid caseId, CancellationToken ct)
    {
        var c = await GetCase(caseId, ct);
        c.Status = CaseStatus.InReview;
        await _db.SaveChangesAsync(ct);
        await UpdateCaseSnapshotAsync(c.Id, c.Status.ToString(), ct);
        return (c.Id, c.Status.ToString());
    }

    public async Task<(Guid Id, string Status)> ApproveCaseAsync(Guid caseId, CancellationToken ct)
    {
        var c = await GetCase(caseId, ct);
        c.Status = CaseStatus.Approved;
        await _db.SaveChangesAsync(ct);
        await UpdateCaseSnapshotAsync(c.Id, c.Status.ToString(), ct);
        return (c.Id, c.Status.ToString());
    }

    public async Task<(Guid Id, string Status)> RejectCaseAsync(Guid caseId, CancellationToken ct)
    {
        var c = await GetCase(caseId, ct);
        c.Status = CaseStatus.Rejected;
        await _db.SaveChangesAsync(ct);
        await UpdateCaseSnapshotAsync(c.Id, c.Status.ToString(), ct);
        return (c.Id, c.Status.ToString());
    }

    public async Task<(Guid Id, string Status)> PreparePublicationAsync(Guid caseId, CancellationToken ct)
    {
        var c = await GetCase(caseId, ct);
        c.Status = CaseStatus.InReview;
        await _db.SaveChangesAsync(ct);
        await UpdateCaseSnapshotAsync(c.Id, c.Status.ToString(), ct);
        return (c.Id, c.Status.ToString());
    }

    public async Task<(Guid Id, string Status)> PublishCaseAsync(Guid caseId, CancellationToken ct)
    {
        var c = await GetCase(caseId, ct);
        c.Status = CaseStatus.Published;
        await _db.SaveChangesAsync(ct);
        await UpdateCaseSnapshotAsync(c.Id, c.Status.ToString(), ct);
        return (c.Id, c.Status.ToString());
    }

    public async Task<(Guid Id, string Status)> HoldCaseAsync(Guid caseId, CancellationToken ct)
    {
        var c = await GetCase(caseId, ct);
        c.Status = CaseStatus.OnHold;
        await _db.SaveChangesAsync(ct);
        await UpdateCaseSnapshotAsync(c.Id, c.Status.ToString(), ct);
        return (c.Id, c.Status.ToString());
    }

    public async Task<(Guid Id, string Status)> ResolveContradictionAsync(Guid id, CancellationToken ct)
    {
        var c = await _db.Contradictions.FirstAsync(x => x.Id == id, ct);
        c.Status = ContradictionStatus.Resolved;
        await _db.SaveChangesAsync(ct);
        await UpdateContradictionSnapshotAsync(c.Id, c.Status.ToString(), ct);
        return (c.Id, c.Status.ToString());
    }

    public async Task<(Guid Id, string Status)> EscalateContradictionAsync(Guid id, CancellationToken ct)
    {
        var c = await _db.Contradictions.FirstAsync(x => x.Id == id, ct);
        c.Status = ParseEnumOrFirst<ContradictionStatus>("Open", "UnderReview", "Draft", "Pending", "Created");
        await _db.SaveChangesAsync(ct);
        await UpdateContradictionSnapshotAsync(c.Id, c.Status.ToString(), ct);
        return (c.Id, c.Status.ToString());
    }

    public async Task<(Guid Id, string Status)> SendClaimToReviewAsync(Guid id, CancellationToken ct)
    {
        var c = await _db.Claims.FirstAsync(x => x.Id == id, ct);
        c.Status = ClaimStatus.Draft;
        await _db.SaveChangesAsync(ct);
        return (c.Id, c.Status.ToString());
    }

    public async Task<(Guid Id, string Status)> ReturnClaimForEditAsync(Guid id, CancellationToken ct)
    {
        var c = await _db.Claims.FirstAsync(x => x.Id == id, ct);
        c.Status = ClaimStatus.Draft;
        await _db.SaveChangesAsync(ct);
        return (c.Id, c.Status.ToString());
    }

    public async Task<(Guid Id, string Status)> CompleteReviewAsync(Guid id, CancellationToken ct)
    {
        var r = await _db.Reviews.FirstAsync(x => x.Id == id, ct);
        r.Status = ReviewStatus.Completed;
        await _db.SaveChangesAsync(ct);
        return (r.Id, r.Status.ToString());
    }

    public async Task<(Guid Id, string Status)> ReopenReviewAsync(Guid id, CancellationToken ct)
    {
        var r = await _db.Reviews.FirstAsync(x => x.Id == id, ct);
        r.Status = ParseEnumOrFirst<ReviewStatus>("Created", "Open", "Pending", "Draft", "InProgress");
        await _db.SaveChangesAsync(ct);
        return (r.Id, r.Status.ToString());
    }

    private async Task<Case> GetCase(Guid id, CancellationToken ct)
    {
        return await _db.Cases.FirstAsync(x => x.Id == id, ct);
    }

    private async Task UpdateCaseSnapshotAsync(Guid caseId, string caseStatus, CancellationToken ct)
    {
        var snapshot = await _scenarioPersistenceService.LoadSnapshotAsync(ct);
        if (snapshot is null || snapshot.CaseId != caseId)
        {
            return;
        }

        var updated = snapshot with
        {
            CaseStatus = caseStatus
        };

        await _scenarioPersistenceService.SaveSnapshotAsync(updated, ct);
    }

    private async Task UpdateContradictionSnapshotAsync(Guid contradictionId, string contradictionStatus, CancellationToken ct)
    {
        var snapshot = await _scenarioPersistenceService.LoadSnapshotAsync(ct);
        if (snapshot is null || snapshot.ContradictionId != contradictionId)
        {
            return;
        }

        var updated = snapshot with
        {
            ContradictionStatus = contradictionStatus
        };

        await _scenarioPersistenceService.SaveSnapshotAsync(updated, ct);
    }
    private static TEnum ParseEnumOrFirst<TEnum>(params string[] preferredNames) where TEnum : struct, Enum
    {
        foreach (var name in preferredNames)
        {
            if (Enum.TryParse<TEnum>(name, true, out var parsed))
            {
                return parsed;
            }
        }

        return Enum.GetValues<TEnum>()[0];
    }
}