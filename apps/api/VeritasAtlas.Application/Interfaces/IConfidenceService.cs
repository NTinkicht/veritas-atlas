using VeritasAtlas.Domain.Entities;

namespace VeritasAtlas.Application.Interfaces;

public interface IConfidenceService
{
    Task<ConfidenceScore> CalculateConfidenceAsync(Guid caseId, string? createdBy = null, CancellationToken cancellationToken = default);
}
