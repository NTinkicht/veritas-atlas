using Microsoft.EntityFrameworkCore;
using VeritasAtlas.Application.Common;
using VeritasAtlas.Application.Interfaces;
using VeritasAtlas.Application.Models;
using VeritasAtlas.Domain.Entities;
using VeritasAtlas.Domain.Enums;
using VeritasAtlas.Infrastructure.Persistence;

namespace VeritasAtlas.Infrastructure.Services;

public sealed class ReviewService : IReviewService
{
    private readonly VeritasAtlasDbContext _dbContext;

    public ReviewService(VeritasAtlasDbContext dbContext)
    {
        _dbContext = dbContext;
    }

    public async Task<PagedResult<Review>> GetReviewsAsync(ReviewListFilters filters, CancellationToken cancellationToken = default)
    {
        var query = _dbContext.Reviews.AsNoTracking().AsQueryable();

        if (!string.IsNullOrWhiteSpace(filters.Status) &&
            Enum.TryParse<ReviewStatus>(filters.Status, true, out var status))
        {
            query = query.Where(x => x.Status == status);
        }

        if (filters.CaseId.HasValue)
        {
            query = query.Where(x => x.CaseId == filters.CaseId.Value);
        }

        query = query.OrderByDescending(x => x.CreatedAtUtc);

        var totalCount = await query.CountAsync(cancellationToken);
        var items = await query
            .Skip((filters.Page - 1) * filters.PageSize)
            .Take(filters.PageSize)
            .ToListAsync(cancellationToken);

        return new PagedResult<Review>
        {
            Items = items,
            Page = filters.Page,
            PageSize = filters.PageSize,
            TotalCount = totalCount,
            TotalPages = (int)Math.Ceiling(totalCount / (double)filters.PageSize)
        };
    }

    public async Task<Review> SubmitForReviewAsync(Guid caseId, string? submittedBy = null, string? notes = null, CancellationToken cancellationToken = default)
    {
        var caseExists = await _dbContext.Cases.AnyAsync(x => x.Id == caseId, cancellationToken);
        if (!caseExists)
        {
            throw new NotFoundException($"Case '{caseId}' was not found.");
        }

        var entity = new Review
        {
            CaseId = caseId,
            Type = ReviewType.Editorial,
            Status = ReviewStatus.Pending,
            Decision = ReviewDecision.None,
            Reviewer = submittedBy ?? "system",
            Notes = notes
        };

        _dbContext.Reviews.Add(entity);
        await _dbContext.SaveChangesAsync(cancellationToken);

        return entity;
    }

    public async Task<Review> CompleteReviewAsync(
        Guid reviewId,
        ReviewDecision decision,
        string? reviewer = null,
        string? notes = null,
        CancellationToken cancellationToken = default)
    {
        var entity = await _dbContext.Reviews.FirstOrDefaultAsync(x => x.Id == reviewId, cancellationToken);
        if (entity is null)
        {
            throw new NotFoundException($"Review '{reviewId}' was not found.");
        }

        entity.Decision = decision;
        entity.Status = ReviewStatus.Completed;
        entity.Reviewer = reviewer ?? entity.Reviewer;
        entity.Notes = notes ?? entity.Notes;
        entity.ReviewedAtUtc = DateTime.UtcNow;
        entity.UpdatedAtUtc = DateTime.UtcNow;

        await _dbContext.SaveChangesAsync(cancellationToken);

        return entity;
    }
}
