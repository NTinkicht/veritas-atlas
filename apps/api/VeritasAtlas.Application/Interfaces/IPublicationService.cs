using VeritasAtlas.Domain.Entities;

namespace VeritasAtlas.Application.Interfaces;

public interface IPublicationService
{
    Task<Publication> PublishCaseAsync(Guid caseId, string? createdBy = null, CancellationToken cancellationToken = default);
}
