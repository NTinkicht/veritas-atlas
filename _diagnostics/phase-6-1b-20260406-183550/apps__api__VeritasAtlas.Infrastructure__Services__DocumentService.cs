using Microsoft.EntityFrameworkCore;
using VeritasAtlas.Application.Interfaces;
using VeritasAtlas.Application.Models;
using VeritasAtlas.Domain.Entities;
using VeritasAtlas.Domain.Enums;
using VeritasAtlas.Infrastructure.Persistence;

namespace VeritasAtlas.Application.Services;

public sealed class DocumentService : IDocumentService
{
    private readonly VeritasAtlasDbContext _dbContext;

    public DocumentService(VeritasAtlasDbContext dbContext)
    {
        _dbContext = dbContext;
    }

    public async Task<Document> AddDocumentAsync(
        Guid sourceId,
        string title,
        string content,
        string? externalReference = null,
        string? createdBy = null,
        CancellationToken cancellationToken = default)
    {
        var entity = new Document
        {
            Id = Guid.NewGuid(),
            SourceId = sourceId,
            Title = title,
            Content = content,
            ExternalReference = externalReference,
            Status = DocumentStatus.Active,
            CreatedBy = createdBy,
            UpdatedBy = createdBy,
            CreatedAtUtc = DateTime.UtcNow,
            UpdatedAtUtc = DateTime.UtcNow
        };

        _dbContext.Documents.Add(entity);
        await _dbContext.SaveChangesAsync(cancellationToken);

        return entity;
    }

    public async Task<PagedListResult<Document>> GetDocumentsAsync(
        int page,
        int pageSize,
        CancellationToken cancellationToken = default)
    {
        page = page < 1 ? 1 : page;
        pageSize = pageSize < 1 ? 20 : pageSize;

        var query = _dbContext.Documents
            .AsNoTracking()
            .OrderByDescending(x => x.CreatedAtUtc);

        var totalCount = await query.CountAsync(cancellationToken);
        var items = await query
            .Skip((page - 1) * pageSize)
            .Take(pageSize)
            .ToListAsync(cancellationToken);

        return new PagedListResult<Document>
        {
            Items = items,
            Page = page,
            PageSize = pageSize,
            TotalCount = totalCount,
            TotalPages = totalCount == 0 ? 0 : (int)Math.Ceiling(totalCount / (double)pageSize)
        };
    }
}
