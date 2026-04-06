using Microsoft.EntityFrameworkCore;
using VeritasAtlas.Application.Common;
using VeritasAtlas.Application.Interfaces;
using VeritasAtlas.Domain.Entities;
using VeritasAtlas.Domain.Enums;
using VeritasAtlas.Domain.ValueObjects;
using VeritasAtlas.Infrastructure.Persistence;

namespace VeritasAtlas.Infrastructure.Services;

public sealed class StatementService : IStatementService
{
    private readonly VeritasAtlasDbContext _dbContext;

    public StatementService(VeritasAtlasDbContext dbContext)
    {
        _dbContext = dbContext;
    }

    public async Task<Statement> ExtractStatementAsync(
        Guid evidenceId,
        string text,
        string? createdBy = null,
        CancellationToken cancellationToken = default)
    {
        var evidence = await _dbContext.Evidences.FirstOrDefaultAsync(x => x.Id == evidenceId, cancellationToken);
        if (evidence is null)
        {
            throw new NotFoundException($"Evidence '{evidenceId}' was not found.");
        }

        if (string.IsNullOrWhiteSpace(text))
        {
            throw new ValidationException("Statement text is required.");
        }

        var entity = new Statement
        {
            EvidenceId = evidenceId,
            DocumentId = evidence.DocumentId,
            Text = new StatementText(text.Trim(), text.Trim()),
            Polarity = StatementPolarity.Affirmative,
            Status = StatementStatus.Extracted,
            Topic = "general"
        };

        _dbContext.Statements.Add(entity);
        await _dbContext.SaveChangesAsync(cancellationToken);

        return entity;
    }
}
