using VeritasAtlas.Domain.Entities;

namespace VeritasAtlas.Application.Interfaces;

public interface IClaimService
{
    Task<Claim> CreateClaimAsync(
        Guid statementId,
        string text,
        Guid? personId = null,
        string? createdBy = null,
        CancellationToken cancellationToken = default);
}
