using Microsoft.EntityFrameworkCore;
using VeritasAtlas.Domain.Entities;
using VeritasAtlas.Domain.Enums;
using VeritasAtlas.Infrastructure.Persistence;

namespace VeritasAtlas.Infrastructure.Services;

public sealed class ClaimSliceService
{
    private readonly VeritasAtlasDbContext _dbContext;

    public ClaimSliceService(VeritasAtlasDbContext dbContext)
    {
        _dbContext = dbContext;
    }

    public async Task<Claim> CreateClaimAsync(
        Guid statementId,
        string topic,
        string normalizedText,
        string? type,
        Guid? personId,
        Guid? caseId,
        bool isMaterial,
        CancellationToken cancellationToken = default)
    {
        var statement = await _dbContext.Statements
            .AsNoTracking()
            .FirstOrDefaultAsync(x => x.Id == statementId, cancellationToken);

        if (statement is null)
        {
            throw new InvalidOperationException($"Statement '{statementId}' was not found.");
        }

        ClaimType parsedType = ClaimType.Factual;
        if (!string.IsNullOrWhiteSpace(type) && Enum.TryParse<ClaimType>(type, true, out var explicitType))
        {
            parsedType = explicitType;
        }

        var entity = new Claim
        {
            StatementId = statementId,
            PersonId = personId,
            CaseId = caseId,
            Type = parsedType,
            Status = ClaimStatus.Draft,
            Topic = topic.Trim(),
            NormalizedText = normalizedText.Trim(),
            IsMaterial = isMaterial
        };

        _dbContext.Claims.Add(entity);
        await _dbContext.SaveChangesAsync(cancellationToken);

        return entity;
    }

    public async Task<(int Total, List<Claim> Items)> GetClaimsAsync(
        int page,
        int pageSize,
        Guid? statementId = null,
        CancellationToken cancellationToken = default)
    {
        page = page < 1 ? 1 : page;
        pageSize = pageSize < 1 ? 20 : pageSize;

        IQueryable<Claim> query = _dbContext.Claims.AsNoTracking();

        if (statementId.HasValue)
        {
            query = query.Where(x => x.StatementId == statementId.Value);
        }

        query = query.OrderByDescending(x => x.CreatedAtUtc);

        var total = await query.CountAsync(cancellationToken);
        var items = await query
            .Skip((page - 1) * pageSize)
            .Take(pageSize)
            .ToListAsync(cancellationToken);

        return (total, items);
    }

    public async Task<Claim?> GetClaimByIdAsync(
        Guid id,
        CancellationToken cancellationToken = default)
    {
        return await _dbContext.Claims
            .AsNoTracking()
            .FirstOrDefaultAsync(x => x.Id == id, cancellationToken);
    }
}
