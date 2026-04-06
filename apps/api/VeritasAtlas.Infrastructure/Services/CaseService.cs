using Microsoft.EntityFrameworkCore;
using VeritasAtlas.Application.Common;
using VeritasAtlas.Application.Interfaces;
using VeritasAtlas.Application.Models;
using VeritasAtlas.Domain.Entities;
using VeritasAtlas.Domain.Enums;
using VeritasAtlas.Infrastructure.Persistence;

namespace VeritasAtlas.Infrastructure.Services;

public sealed class CaseService : ICaseService
{
    private readonly VeritasAtlasDbContext _dbContext;

    public CaseService(VeritasAtlasDbContext dbContext)
    {
        _dbContext = dbContext;
    }

    public async Task<PagedResult<Case>> GetCasesAsync(CaseListFilters filters, CancellationToken cancellationToken = default)
    {
        var query = _dbContext.Cases
            .Include(x => x.SubjectPerson)
            .AsNoTracking()
            .AsQueryable();

        if (!string.IsNullOrWhiteSpace(filters.Status) &&
            Enum.TryParse<CaseStatus>(filters.Status, true, out var parsedStatus))
        {
            query = query.Where(x => x.Status == parsedStatus);
        }

        if (!string.IsNullOrWhiteSpace(filters.Search))
        {
            var search = filters.Search.Trim();
            query = query.Where(x =>
                x.Title.Contains(search) ||
                (x.Summary != null && x.Summary.Contains(search)));
        }

        query = (filters.SortBy.ToLowerInvariant(), filters.SortDirection.ToLowerInvariant()) switch
        {
            ("id", "asc") => query.OrderBy(x => x.Id),
            ("id", _) => query.OrderByDescending(x => x.Id),
            ("status", "asc") => query.OrderBy(x => x.Status),
            ("status", _) => query.OrderByDescending(x => x.Status),
            ("createdat", "asc") => query.OrderBy(x => x.CreatedAtUtc),
            _ => query.OrderByDescending(x => x.CreatedAtUtc)
        };

        var totalCount = await query.CountAsync(cancellationToken);
        var items = await query
            .Skip((filters.Page - 1) * filters.PageSize)
            .Take(filters.PageSize)
            .ToListAsync(cancellationToken);

        return new PagedResult<Case>
        {
            Items = items,
            Page = filters.Page,
            PageSize = filters.PageSize,
            TotalCount = totalCount,
            TotalPages = (int)Math.Ceiling(totalCount / (double)filters.PageSize)
        };
    }

    public async Task<Case> GetCaseByIdAsync(Guid id, CancellationToken cancellationToken = default)
    {
        var entity = await _dbContext.Cases
            .Include(x => x.Claims)
            .Include(x => x.Contradictions)
            .Include(x => x.Reviews)
            .Include(x => x.Publications)
            .FirstOrDefaultAsync(x => x.Id == id, cancellationToken);

        if (entity is null)
        {
            throw new NotFoundException($"Case '{id}' was not found.");
        }

        return entity;
    }

    public async Task<Case> CreateCaseAsync(
        string title,
        string? summary = null,
        Guid? subjectPersonId = null,
        string? createdBy = null,
        CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(title))
        {
            throw new ValidationException("Case title is required.");
        }

        var entity = new Case
        {
            Title = title.Trim(),
            Summary = summary,
            Type = CaseType.ContradictionReview,
            Status = CaseStatus.Open,
            SubjectPersonId = subjectPersonId
        };

        _dbContext.Cases.Add(entity);
        await _dbContext.SaveChangesAsync(cancellationToken);

        return entity;
    }

    public async Task AddClaimToCaseAsync(Guid caseId, Guid claimId, string? createdBy = null, CancellationToken cancellationToken = default)
    {
        var entity = await _dbContext.Claims.FirstOrDefaultAsync(x => x.Id == claimId, cancellationToken);
        if (entity is null)
        {
            throw new NotFoundException($"Claim '{claimId}' was not found.");
        }

        var caseExists = await _dbContext.Cases.AnyAsync(x => x.Id == caseId, cancellationToken);
        if (!caseExists)
        {
            throw new NotFoundException($"Case '{caseId}' was not found.");
        }

        entity.CaseId = caseId;
        entity.UpdatedAtUtc = DateTime.UtcNow;

        await _dbContext.SaveChangesAsync(cancellationToken);
    }

    public async Task ApproveCaseAsync(Guid caseId, string? approvedBy = null, string? notes = null, CancellationToken cancellationToken = default)
    {
        var entity = await _dbContext.Cases.FirstOrDefaultAsync(x => x.Id == caseId, cancellationToken);
        if (entity is null)
        {
            throw new NotFoundException($"Case '{caseId}' was not found.");
        }

        entity.Status = CaseStatus.Approved;
        entity.UpdatedAtUtc = DateTime.UtcNow;

        await _dbContext.SaveChangesAsync(cancellationToken);
    }

    public async Task RejectCaseAsync(Guid caseId, string? rejectedBy = null, string? notes = null, CancellationToken cancellationToken = default)
    {
        var entity = await _dbContext.Cases.FirstOrDefaultAsync(x => x.Id == caseId, cancellationToken);
        if (entity is null)
        {
            throw new NotFoundException($"Case '{caseId}' was not found.");
        }

        entity.Status = CaseStatus.Rejected;
        entity.UpdatedAtUtc = DateTime.UtcNow;

        await _dbContext.SaveChangesAsync(cancellationToken);
    }
}
