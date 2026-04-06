using Microsoft.AspNetCore.Mvc;
using VeritasAtlas.Api.Contracts.Documents;
using VeritasAtlas.Application.Interfaces;
using VeritasAtlas.Domain.Enums;

namespace VeritasAtlas.Api.Controllers;

[ApiController]
[Route("api/v1/documents")]
public sealed class DocumentsController : ControllerBase
{
    private readonly IDocumentService _documentService;

    public DocumentsController(IDocumentService documentService)
    {
        _documentService = documentService;
    }

    [HttpGet]
    public async Task<ActionResult<GetDocumentsResponse>> GetDocuments(
        [FromQuery] int page = 1,
        [FromQuery] int pageSize = 20,
        [FromQuery] Guid? sourceId = null,
        [FromQuery] string? status = null,
        CancellationToken cancellationToken = default)
    {
        DocumentStatus? parsedStatus = null;
        if (!string.IsNullOrWhiteSpace(status))
        {
            if (!Enum.TryParse<DocumentStatus>(status, true, out var documentStatus))
            {
                return BadRequest(new { message = "Invalid document status filter." });
            }
            parsedStatus = documentStatus;
        }

        var result = await _documentService.GetDocumentsAsync(
            page,
            pageSize,
            sourceId,
            parsedStatus,
            cancellationToken);

        var items = result.Items
            .Select(entity => new GetDocumentsItemResponse(
                entity.Id,
                entity.SourceId,
                entity.Title,
                entity.Status.ToString(),
                entity.CreatedAtUtc,
                entity.UpdatedAtUtc))
            .ToArray();

        return Ok(new GetDocumentsResponse(
            items,
            result.Page,
            result.PageSize,
            result.TotalCount,
            result.TotalPages));
    }

    [HttpGet("{id:guid}")]
    public async Task<ActionResult<GetDocumentResponse>> GetDocumentById(
        [FromRoute] Guid id,
        CancellationToken cancellationToken = default)
    {
        var result = await _documentService.GetDocumentsAsync(1, int.MaxValue, null, null, cancellationToken);
        var entity = result.Items.FirstOrDefault(x => x.Id == id);

        if (entity is null)
        {
            return NotFound();
        }

        return Ok(new GetDocumentResponse(
            entity.Id,
            entity.SourceId,
            entity.Title,
            entity.Type.ToString(),
            entity.Status.ToString(),
            entity.LanguageCode,
            entity.ExternalId,
            entity.Url,
            entity.ContentHash,
            entity.PublishedAtUtc,
            entity.RetrievedAtUtc,
            entity.CreatedAtUtc,
            entity.UpdatedAtUtc));
    }

    [HttpPost]
    public async Task<ActionResult<CreateDocumentResponse>> CreateDocument(
        [FromBody] CreateDocumentRequest request,
        CancellationToken cancellationToken = default)
    {
        var entity = await _documentService.AddDocumentAsync(
            request.SourceId,
            request.Title,
            request.Content,
            request.ExternalReference,
            request.CreatedBy,
            cancellationToken);

        var response = new CreateDocumentResponse(
            entity.Id,
            entity.SourceId,
            entity.Title,
            entity.Status.ToString(),
            entity.CreatedAtUtc,
            entity.UpdatedAtUtc);

        return Ok(response);
    }
}
