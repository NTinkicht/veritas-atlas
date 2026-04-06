using VeritasAtlas.Application.Models;
using VeritasAtlas.Domain.Entities;
using VeritasAtlas.Domain.Enums;

namespace VeritasAtlas.Application.Interfaces;

public interface IDocumentService
{
    Task<Document> AddDocumentAsync(
        Guid sourceId,
        string title,
        string content,
        string? externalReference = null,
        string? createdBy = null,
        CancellationToken cancellationToken = default);

    Task<PagedListResult<Document>> GetDocumentsAsync(
        int page,
        int pageSize,
        Guid? sourceId = null,
        DocumentStatus? status = null,
        CancellationToken cancellationToken = default);
}
