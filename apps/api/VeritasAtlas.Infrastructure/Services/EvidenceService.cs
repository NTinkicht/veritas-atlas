using Microsoft.EntityFrameworkCore;
using System.Security.Cryptography;
using System.Text;
using VeritasAtlas.Application.Interfaces;
using VeritasAtlas.Application.Models;
using VeritasAtlas.Domain.Entities;
using VeritasAtlas.Domain.Enums;
using VeritasAtlas.Domain.ValueObjects;
using VeritasAtlas.Infrastructure.Persistence;

namespace VeritasAtlas.Infrastructure.Services;

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
        var document = await _dbContext.Documents
            .AsNoTracking()
            .FirstOrDefaultAsync(x => x.Id == documentId, cancellationToken);

        if (document is null)
        {
            throw new InvalidOperationException($"Document '{documentId}' was not found.");
        }

        var content = string.IsNullOrWhiteSpace(context)
            ? quote
            : quote + Environment.NewLine + Environment.NewLine + context;

        var entity = new Evidence
        {
            SourceId = document.SourceId,
            DocumentId = documentId,
            Type = default,
            Status = default,
            Content = content,
            ContentHash = ComputeSha256(content),
            LanguageCode = document.LanguageCode,
            Span = (startOffset.HasValue && endOffset.HasValue)
                ? new DocumentSpan(startOffset.Value, endOffset.Value)
                : null,
            CapturedAtUtc = DateTime.UtcNow
        };

        _dbContext.Evidences.Add(entity);
        await _dbContext.SaveChangesAsync(cancellationToken);

        return entity;
    }

    public async Task<PagedListResult<Evidence>> GetEvidenceAsync(
        int page,
        int pageSize,
        Guid? documentId = null,
        EvidenceStatus? status = null,
        CancellationToken cancellationToken = default)
    {
        page = page < 1 ? 1 : page;
        pageSize = pageSize < 1 ? 20 : pageSize;

        IQueryable<Evidence> query = _dbContext.Evidences.AsNoTracking();

        if (documentId.HasValue)
        {
            query = query.Where(x => x.DocumentId == documentId.Value);
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

        return new PagedListResult<Evidence>
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
