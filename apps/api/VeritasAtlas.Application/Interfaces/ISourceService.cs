using VeritasAtlas.Application.Models;
using VeritasAtlas.Domain.Entities;
using VeritasAtlas.Domain.Enums;

namespace VeritasAtlas.Application.Interfaces;

public interface ISourceService
{
    Task<Source> RegisterSourceAsync(
        string name,
        SourceType type,
        string reference,
        string? createdBy = null,
        CancellationToken cancellationToken = default);

    Task<PagedListResult<Source>> GetSourcesAsync(
        int page,
        int pageSize,
        string? search = null,
        SourceType? type = null,
        SourceStatus? status = null,
        CancellationToken cancellationToken = default);
}
