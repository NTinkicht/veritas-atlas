using VeritasAtlas.Application.Models;
using VeritasAtlas.Domain.Entities;

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
        CancellationToken cancellationToken = default);
}
