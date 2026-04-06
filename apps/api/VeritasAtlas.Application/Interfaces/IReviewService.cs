using VeritasAtlas.Application.Models;
using VeritasAtlas.Domain.Entities;
using VeritasAtlas.Domain.Enums;

namespace VeritasAtlas.Application.Interfaces;

public interface IReviewService
{
    Task<PagedResult<Review>> GetReviewsAsync(ReviewListFilters filters, CancellationToken cancellationToken = default);
    Task<Review> SubmitForReviewAsync(Guid caseId, string? submittedBy = null, string? notes = null, CancellationToken cancellationToken = default);
    Task<Review> CompleteReviewAsync(
        Guid reviewId,
        ReviewDecision decision,
        string? reviewer = null,
        string? notes = null,
        CancellationToken cancellationToken = default);
}
