using Microsoft.EntityFrameworkCore;
using VeritasAtlas.Domain.Entities;
using VeritasAtlas.Domain.Enums;
using VeritasAtlas.Infrastructure.Persistence;

namespace VeritasAtlas.Infrastructure.Services;

public sealed class ContradictionSliceService
{
    private readonly VeritasAtlasDbContext _dbContext;

    public ContradictionSliceService(VeritasAtlasDbContext dbContext)
    {
        _dbContext = dbContext;
    }

    public async Task<Contradiction> CreateContradictionAsync(
        Guid primaryClaimId,
        Guid secondaryClaimId,
        string topic,
        string summary,
        string? contradictionType,
        string? severity,
        Guid? caseId,
        CancellationToken cancellationToken = default)
    {
        var primaryClaim = await _dbContext.Claims
            .AsNoTracking()
            .FirstOrDefaultAsync(x => x.Id == primaryClaimId, cancellationToken);

        var secondaryClaim = await _dbContext.Claims
            .AsNoTracking()
            .FirstOrDefaultAsync(x => x.Id == secondaryClaimId, cancellationToken);

        if (primaryClaim is null)
        {
            throw new InvalidOperationException($"Primary claim '{primaryClaimId}' was not found.");
        }

        if (secondaryClaim is null)
        {
            throw new InvalidOperationException($"Secondary claim '{secondaryClaimId}' was not found.");
        }

        var parsedType = Enum.TryParse<ContradictionType>(contradictionType, true, out var contradictionTypeValue)
            ? contradictionTypeValue
            : ContradictionType.Direct;

        var parsedSeverity = Enum.TryParse<ContradictionSeverity>(severity, true, out var contradictionSeverityValue)
            ? contradictionSeverityValue
            : ContradictionSeverity.Medium;

        var resolvedCaseId = caseId ?? primaryClaim.CaseId ?? secondaryClaim.CaseId;
        if (!resolvedCaseId.HasValue)
        {
            throw new InvalidOperationException("A contradiction requires a CaseId. Provide one explicitly or use claims already linked to a case.");
        }

        var entity = new Contradiction
        {
            CaseId = resolvedCaseId.Value,
            LeftClaimId = primaryClaimId,
            RightClaimId = secondaryClaimId,
            Type = parsedType,
            Severity = parsedSeverity,
            Status = ContradictionStatus.Draft,
            Summary = string.IsNullOrWhiteSpace(summary) ? topic.Trim() : summary.Trim(),
            Rationale = null,
            ConfidenceScoreId = null
        };

        _dbContext.Contradictions.Add(entity);
        await _dbContext.SaveChangesAsync(cancellationToken);

        return entity;
    }

    public async Task<(int Total, List<Contradiction> Items)> GetContradictionsAsync(
        int page,
        int pageSize,
        Guid? claimId = null,
        Guid? caseId = null,
        CancellationToken cancellationToken = default)
    {
        page = page < 1 ? 1 : page;
        pageSize = pageSize < 1 ? 20 : pageSize;

        IQueryable<Contradiction> query = _dbContext.Contradictions.AsNoTracking();

        if (claimId.HasValue)
        {
            query = query.Where(x => x.LeftClaimId == claimId.Value || x.RightClaimId == claimId.Value);
        }

        if (caseId.HasValue)
        {
            query = query.Where(x => x.CaseId == caseId.Value);
        }

        query = query.OrderByDescending(x => x.CreatedAtUtc);

        var total = await query.CountAsync(cancellationToken);
        var items = await query
            .Skip((page - 1) * pageSize)
            .Take(pageSize)
            .ToListAsync(cancellationToken);

        return (total, items);
    }

    public async Task<Contradiction?> GetContradictionByIdAsync(
        Guid id,
        CancellationToken cancellationToken = default)
    {
        return await _dbContext.Contradictions
            .AsNoTracking()
            .FirstOrDefaultAsync(x => x.Id == id, cancellationToken);
    }
}