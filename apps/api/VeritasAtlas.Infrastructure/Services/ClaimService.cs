using Microsoft.EntityFrameworkCore;
using VeritasAtlas.Application.Common;
using VeritasAtlas.Application.Interfaces;
using VeritasAtlas.Domain.Entities;
using VeritasAtlas.Domain.Enums;
using VeritasAtlas.Infrastructure.Persistence;

namespace VeritasAtlas.Infrastructure.Services;

public sealed class ClaimService : IClaimService
{
    private readonly VeritasAtlasDbContext _dbContext;

    public ClaimService(VeritasAtlasDbContext dbContext)
    {
        _dbContext = dbContext;
    }

    public async Task<Claim> CreateClaimAsync(
        Guid statementId,
        string text,
        Guid? personId = null,
        string? createdBy = null,
        CancellationToken cancellationToken = default)
    {
        var statement = await _dbContext.Statements.FirstOrDefaultAsync(x => x.Id == statementId, cancellationToken);
        if (statement is null)
        {
            throw new NotFoundException($"Statement '{statementId}' was not found.");
        }

        if (string.IsNullOrWhiteSpace(text))
        {
            throw new ValidationException("Claim text is required.");
        }

        var entity = new Claim
        {
            StatementId = statementId,
            PersonId = personId,
            Type = ClaimType.Factual,
            Status = ClaimStatus.Active,
            Topic = statement.Topic ?? "general",
            NormalizedText = text.Trim(),
            IsMaterial = true
        };

        _dbContext.Claims.Add(entity);
        await _dbContext.SaveChangesAsync(cancellationToken);

        return entity;
    }
}
