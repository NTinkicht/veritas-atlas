using Microsoft.EntityFrameworkCore;
using System.Security.Cryptography;
using System.Text;
using VeritasAtlas.Application.Interfaces;
using VeritasAtlas.Application.Models;
using VeritasAtlas.Domain.Entities;
using VeritasAtlas.Domain.Enums;
using VeritasAtlas.Infrastructure.Persistence;

namespace VeritasAtlas.Infrastructure.Services;

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
        string? externalId = null;
        string? url = null;

        if (!string.IsNullOrWhiteSpace(externalReference))
        {
            if (Uri.TryCreate(externalReference, UriKind.Absolute, out var uri))
            {
                url = uri.ToString();
            }
            else
            {
                externalId = externalReference;
            }
        }

        var entity = new Document
        {
            SourceId = sourceId,
            Title = title,
            Type = default,
            Status = default,
            ExternalId = externalId,
            Url = url,
            ContentHash = ComputeSha256(content),
            RetrievedAtUtc = DateTime.UtcNow
        };

        _dbContext.Documents.Add(entity);
        await _dbContext.SaveChangesAsync(cancellationToken);

        return entity;
    }

    public async Task<PagedListResult<Document>> GetDocumentsAsync(
        int page,
        int pageSize,
        Guid? sourceId = null,
        DocumentStatus? status = null,
        CancellationToken cancellationToken = default)
    {
        page = page < 1 ? 1 : page;
        pageSize = pageSize < 1 ? 20 : pageSize;

        IQueryable<Document> query = _dbContext.Documents.AsNoTracking();

        if (sourceId.HasValue)
        {
            query = query.Where(x => x.SourceId == sourceId.Value);
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

        return new PagedListResult<Document>
        {
            Items = items,
            Page = page,
            PageSize = pageSize,
            TotalCount = totalCount,
            TotalPages = totalCount == 0 ? 0 : (int)Math.Ceiling(totalCount / (double)pageSize)
        };
    }

    private static string? ComputeSha256(string? input)
    {
        if (string.IsNullOrWhiteSpace(input))
        {
            return null;
        }

        var bytes = SHA256.HashData(Encoding.UTF8.GetBytes(input));
        return Convert.ToHexString(bytes);
    }
}
