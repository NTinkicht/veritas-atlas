using Microsoft.EntityFrameworkCore;
using VeritasAtlas.Application.Common;
using VeritasAtlas.Application.Interfaces;
using VeritasAtlas.Domain.Entities;
using VeritasAtlas.Domain.Enums;
using VeritasAtlas.Infrastructure.Persistence;

namespace VeritasAtlas.Infrastructure.Services;

public sealed class ContradictionService : IContradictionService
{
    private readonly VeritasAtlasDbContext _dbContext;

    public ContradictionService(VeritasAtlasDbContext dbContext)
    {
        _dbContext = dbContext;
    }

    public async Task<Contradiction> AddContradictionAsync(
        Guid caseId,
        Guid leftClaimId,
        Guid rightClaimId,
        ContradictionType type,
        string summary,
        string? rationale = null,
        string? createdBy = null,
        CancellationToken cancellationToken = default)
    {
        var caseExists = await _dbContext.Cases.AnyAsync(x => x.Id == caseId, cancellationToken);
        if (!caseExists)
        {
            throw new NotFoundException($"Case '{caseId}' was not found.");
        }

        if (leftClaimId == rightClaimId)
        {
            throw new ValidationException("A contradiction requires two different claims.");
        }

        var entity = new Contradiction
        {
            CaseId = caseId,
            LeftClaimId = leftClaimId,
            RightClaimId = rightClaimId,
            Type = type,
            Severity = ContradictionSeverity.Medium,
            Status = ContradictionStatus.Active,
            Summary = summary,
            Rationale = rationale
        };

        _dbContext.Contradictions.Add(entity);
        await _dbContext.SaveChangesAsync(cancellationToken);

        return entity;
    }
}
