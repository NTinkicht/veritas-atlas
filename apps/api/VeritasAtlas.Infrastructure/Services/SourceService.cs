using Microsoft.EntityFrameworkCore;
using VeritasAtlas.Application.Interfaces;
using VeritasAtlas.Application.Models;
using VeritasAtlas.Domain.Entities;
using VeritasAtlas.Domain.Enums;
using VeritasAtlas.Domain.ValueObjects;
using VeritasAtlas.Infrastructure.Persistence;

namespace VeritasAtlas.Infrastructure.Services;

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
            Name = name,
            Type = type,
            Status = default,
            TrustTier = default,
            Reference = BuildReference(reference)
        };

        _dbContext.Sources.Add(entity);
        await _dbContext.SaveChangesAsync(cancellationToken);

        return entity;
    }

    public async Task<PagedListResult<Source>> GetSourcesAsync(
        int page,
        int pageSize,
        string? search = null,
        SourceType? type = null,
        SourceStatus? status = null,
        CancellationToken cancellationToken = default)
    {
        page = page < 1 ? 1 : page;
        pageSize = pageSize < 1 ? 20 : pageSize;

        IQueryable<Source> query = _dbContext.Sources.AsNoTracking();

        if (!string.IsNullOrWhiteSpace(search))
        {
            var term = search.Trim().ToLower();

            query = query.Where(x =>
                x.Name.ToLower().Contains(term) ||
                (x.Reference != null && (
                    (x.Reference.ExternalId != null && x.Reference.ExternalId.ToLower().Contains(term)) ||
                    (x.Reference.Url != null && x.Reference.Url.ToLower().Contains(term)) ||
                    (x.Reference.Domain != null && x.Reference.Domain.ToLower().Contains(term)) ||
                    (x.Reference.LanguageCode != null && x.Reference.LanguageCode.ToLower().Contains(term))
                )));
        }

        if (type.HasValue)
        {
            query = query.Where(x => x.Type == type.Value);
        }

        if (status.HasValue)
        {
            query = query.Where(x => x.Status == status.Value);
        }

        query = query.OrderByDescending(x => x.CreatedAtUtc);

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

    private static SourceReference? BuildReference(string reference)
    {
        if (string.IsNullOrWhiteSpace(reference))
        {
            return null;
        }

        if (Uri.TryCreate(reference, UriKind.Absolute, out var uri))
        {
            return new SourceReference(
                null,
                uri.ToString(),
                uri.Host,
                null);
        }

        return new SourceReference(
            reference,
            null,
            null,
            null);
    }
}
