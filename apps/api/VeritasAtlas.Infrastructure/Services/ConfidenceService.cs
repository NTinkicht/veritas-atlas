using Microsoft.EntityFrameworkCore;
using VeritasAtlas.Application.Common;
using VeritasAtlas.Application.Interfaces;
using VeritasAtlas.Domain.Entities;
using VeritasAtlas.Domain.Enums;
using VeritasAtlas.Domain.ValueObjects;
using VeritasAtlas.Infrastructure.Persistence;

namespace VeritasAtlas.Infrastructure.Services;

public sealed class ConfidenceService : IConfidenceService
{
    private readonly VeritasAtlasDbContext _dbContext;

    public ConfidenceService(VeritasAtlasDbContext dbContext)
    {
        _dbContext = dbContext;
    }

    public async Task<ConfidenceScore> CalculateConfidenceAsync(Guid caseId, string? createdBy = null, CancellationToken cancellationToken = default)
    {
        var caseExists = await _dbContext.Cases.AnyAsync(x => x.Id == caseId, cancellationToken);
        if (!caseExists)
        {
            throw new NotFoundException($"Case '{caseId}' was not found.");
        }

        var entity = new ConfidenceScore
        {
            CaseId = caseId,
            TargetType = ScoreTargetType.Case,
            TargetId = caseId,
            Value = new ConfidenceValue(0.75m),
            Band = ConfidenceBand.High,
            Status = ConfidenceScoreStatus.Active,
            ModelVersion = "v1",
            Explanation = "Initial baseline confidence score."
        };

        _dbContext.ConfidenceScores.Add(entity);
        await _dbContext.SaveChangesAsync(cancellationToken);

        return entity;
    }
}
