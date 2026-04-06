using Microsoft.EntityFrameworkCore;
using VeritasAtlas.Application.Interfaces;
using VeritasAtlas.Application.Models;
using VeritasAtlas.Domain.Entities;
using VeritasAtlas.Domain.Enums;
using VeritasAtlas.Domain.ValueObjects;
using VeritasAtlas.Infrastructure.Persistence;

namespace VeritasAtlas.Application.Services;

public sealed class EvidenceService : IEvidenceService
{
    private readonly VeritasAtlasDbContext _dbContext;

    public EvidenceService(VeritasAtlasDbContext dbContext)
    {
        _dbContext = dbContext;
    }

    public async Task<Evidence> AddEvidenceAsync(
        Guid documentId,
        string quote,
        int? startOffset = null,
        int? endOffset = null,
        string? context = null,
        string? createdBy = null,
        CancellationToken cancellationToken = default)
    {
        var entity = new Evidence
        {
            Id = Guid.NewGuid(),
            DocumentId = documentId,
            Quote = quote,
            Context = context,
            Span = new DocumentSpan(startOffset, endOffset),
            Status = EvidenceStatus.Active,
            CreatedBy = createdBy,
            UpdatedBy = createdBy,
            CreatedAtUtc = DateTime.UtcNow,
            UpdatedAtUtc = DateTime.UtcNow
        };

        _dbContext.Evidence.Add(entity);
        await _dbContext.SaveChangesAsync(cancellationToken);

        return entity;
    }

    public async Task<PagedListResult<Evidence>> GetEvidenceAsync(
        int page,
        int pageSize,
        CancellationToken cancellationToken = default)
    {
        page = page < 1 ? 1 : page;
        pageSize = pageSize < 1 ? 20 : pageSize;

        var query = _dbContext.Evidence
            .AsNoTracking()
            .OrderByDescending(x => x.CreatedAtUtc);

        var totalCount = await query.CountAsync(cancellationToken);
        var items = await query
            .Skip((page - 1) * pageSize)
            .Take(pageSize)
            .ToListAsync(cancellationToken);

        return new PagedListResult<Evidence>
        {
            Items = items,
            Page = page,
            PageSize = pageSize,
            TotalCount = totalCount,
            TotalPages = totalCount == 0 ? 0 : (int)Math.Ceiling(totalCount / (double)pageSize)
        };
    }
}
