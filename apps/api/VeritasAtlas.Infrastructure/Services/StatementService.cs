using Microsoft.EntityFrameworkCore;
using VeritasAtlas.Application.Common;
using VeritasAtlas.Application.Contracts.Statements;
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
        var evidence = await _dbContext.Evidences
            .FirstOrDefaultAsync(x => x.Id == evidenceId, cancellationToken);

        if (evidence is null)
        {
            throw new NotFoundException($"Evidence '{evidenceId}' was not found.");
        }

        if (string.IsNullOrWhiteSpace(text))
        {
            throw new ValidationException("Statement text is required.");
        }

        var normalized = text.Trim();

        var entity = new Statement
        {
            EvidenceId = evidenceId,
            DocumentId = evidence.DocumentId,
            Text = new StatementText(normalized, normalized),
            Polarity = StatementPolarity.Affirmative,
            Status = StatementStatus.Extracted,
            Topic = "general"
        };

        _dbContext.Statements.Add(entity);
        await _dbContext.SaveChangesAsync(cancellationToken);

        return entity;
    }

    public async Task<(int Total, List<StatementListItemResponse> Items)> GetStatementsAsync(
        int page,
        int pageSize)
    {
        page = page < 1 ? 1 : page;
        pageSize = pageSize < 1 ? 20 : pageSize;

        var query = _dbContext.Statements.AsNoTracking();

        var total = await query.CountAsync();

        var items = await query
            .OrderByDescending(x => x.CreatedAtUtc)
            .Skip((page - 1) * pageSize)
            .Take(pageSize)
            .Select(x => new StatementListItemResponse
            {
                Id = x.Id,
                Text = x.Text.Raw,
                Topic = x.Topic,
                Predicate = x.Predicate,
                Object = x.ObjectValue,
                Polarity = x.Polarity.ToString(),
                Status = x.Status.ToString(),
                CreatedAt = x.CreatedAtUtc
            })
            .ToListAsync();

        return (total, items);
    }

    public async Task<StatementDetailResponse?> GetStatementByIdAsync(Guid id)
    {
        return await _dbContext.Statements
            .AsNoTracking()
            .Where(x => x.Id == id)
            .Select(x => new StatementDetailResponse
            {
                Id = x.Id,
                Text = x.Text.Raw,
                Topic = x.Topic,
                Predicate = x.Predicate,
                Object = x.ObjectValue,
                Polarity = x.Polarity.ToString(),
                Status = x.Status.ToString(),
                EvidenceId = x.EvidenceId,
                PersonId = x.PersonId,
                CreatedAt = x.CreatedAtUtc
            })
            .FirstOrDefaultAsync();
    }
}
