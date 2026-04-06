using Microsoft.EntityFrameworkCore;
using VeritasAtlas.Application.Interfaces;
using VeritasAtlas.Application.Models;
using VeritasAtlas.Domain.Entities;
using VeritasAtlas.Domain.Enums;
using VeritasAtlas.Domain.ValueObjects;
using VeritasAtlas.Infrastructure.Persistence;

namespace VeritasAtlas.Application.Services;

public sealed class SourceService : ISourceService
{
    private readonly VeritasAtlasDbContext _dbContext;

    public SourceService(VeritasAtlasDbContext dbContext)
    {
        _dbContext = dbContext;
    }

    public async Task<Source> RegisterSourceAsync(
        string name,
        SourceType type,
        string reference,
        string? createdBy = null,
        CancellationToken cancellationToken = default)
    {
        var entity = new Source
        {
            Id = Guid.NewGuid(),
            Name = name,
            Type = type,
            Reference = string.IsNullOrWhiteSpace(reference) ? null : new SourceReference(reference),
            Status = SourceStatus.Active,
            CreatedBy = createdBy,
            UpdatedBy = createdBy,
            CreatedAtUtc = DateTime.UtcNow,
            UpdatedAtUtc = DateTime.UtcNow
        };

        _dbContext.Sources.Add(entity);
        await _dbContext.SaveChangesAsync(cancellationToken);

        return entity;
    }

    public async Task<PagedListResult<Source>> GetSourcesAsync(
        int page,
        int pageSize,
        CancellationToken cancellationToken = default)
    {
        page = page < 1 ? 1 : page;
        pageSize = pageSize < 1 ? 20 : pageSize;

        var query = _dbContext.Sources
            .AsNoTracking()
            .OrderByDescending(x => x.CreatedAtUtc);

        var totalCount = await query.CountAsync(cancellationToken);
        var items = await query
            .Skip((page - 1) * pageSize)
            .Take(pageSize)
            .ToListAsync(cancellationToken);

        return new PagedListResult<Source>
        {
            Items = items,
            Page = page,
            PageSize = pageSize,
            TotalCount = totalCount,
            TotalPages = totalCount == 0 ? 0 : (int)Math.Ceiling(totalCount / (double)pageSize)
        };
    }
}
