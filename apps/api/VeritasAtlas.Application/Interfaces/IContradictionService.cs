using VeritasAtlas.Domain.Entities;
using VeritasAtlas.Domain.Enums;

namespace VeritasAtlas.Application.Interfaces;

public interface IContradictionService
{
    Task<Contradiction> AddContradictionAsync(
        Guid caseId,
        Guid leftClaimId,
        Guid rightClaimId,
        ContradictionType type,
        string summary,
        string? rationale = null,
        string? createdBy = null,
        CancellationToken cancellationToken = default);
}
