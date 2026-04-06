using Microsoft.EntityFrameworkCore;
using VeritasAtlas.Application.Common;
using VeritasAtlas.Application.Interfaces;
using VeritasAtlas.Domain.Entities;
using VeritasAtlas.Domain.Enums;
using VeritasAtlas.Infrastructure.Persistence;

namespace VeritasAtlas.Infrastructure.Services;

public sealed class PublicationService : IPublicationService
{
    private readonly VeritasAtlasDbContext _dbContext;

    public PublicationService(VeritasAtlasDbContext dbContext)
    {
        _dbContext = dbContext;
    }

    public async Task<Publication> PublishCaseAsync(Guid caseId, string? createdBy = null, CancellationToken cancellationToken = default)
    {
        var caseEntity = await _dbContext.Cases.FirstOrDefaultAsync(x => x.Id == caseId, cancellationToken);
        if (caseEntity is null)
        {
            throw new NotFoundException($"Case '{caseId}' was not found.");
        }

        var entity = new Publication
        {
            CaseId = caseId,
            Channel = PublicationChannel.Web,
            Status = PublicationStatus.Published,
            Title = caseEntity.Title,
            Slug = $"{caseEntity.Title.Trim().ToLowerInvariant().Replace(" ", "-")}-{caseId.ToString()[..8]}",
            PublishedAtUtc = DateTime.UtcNow
        };

        _dbContext.Publications.Add(entity);

        caseEntity.Status = CaseStatus.Published;
        caseEntity.UpdatedAtUtc = DateTime.UtcNow;

        await _dbContext.SaveChangesAsync(cancellationToken);

        return entity;
    }
}
