using VeritasAtlas.Application.Models;
using VeritasAtlas.Domain.Entities;
using VeritasAtlas.Domain.Enums;

namespace VeritasAtlas.Application.Interfaces;

public interface IEvidenceService
{
    Task<Evidence> AddEvidenceAsync(
        Guid documentId,
        string quote,
        int? startOffset = null,
        int? endOffset = null,
        string? context = null,
        string? createdBy = null,
        CancellationToken cancellationToken = default);

    Task<PagedListResult<Evidence>> GetEvidenceAsync(
        int page,
        int pageSize,
        Guid? documentId = null,
        EvidenceStatus? status = null,
        CancellationToken cancellationToken = default);
}
