using Microsoft.AspNetCore.Mvc;
using VeritasAtlas.Api.Contracts.Evidence;
using VeritasAtlas.Application.Interfaces;
using VeritasAtlas.Domain.Enums;

namespace VeritasAtlas.Api.Controllers;

[ApiController]
[Route("api/v1/evidence")]
public sealed class EvidenceController : ControllerBase
{
    private readonly IEvidenceService _evidenceService;

    public EvidenceController(IEvidenceService evidenceService)
    {
        _evidenceService = evidenceService;
    }

    [HttpGet]
    public async Task<ActionResult<GetEvidenceListResponse>> GetEvidence(
        [FromQuery] int page = 1,
        [FromQuery] int pageSize = 20,
        [FromQuery] Guid? documentId = null,
        [FromQuery] string? status = null,
        CancellationToken cancellationToken = default)
    {
        EvidenceStatus? parsedStatus = null;
        if (!string.IsNullOrWhiteSpace(status))
        {
            if (!Enum.TryParse<EvidenceStatus>(status, true, out var evidenceStatus))
            {
                return BadRequest(new { message = "Invalid evidence status filter." });
            }
            parsedStatus = evidenceStatus;
        }

        var result = await _evidenceService.GetEvidenceAsync(
            page,
            pageSize,
            documentId,
            parsedStatus,
            cancellationToken);

        var items = result.Items
            .Select(entity => new GetEvidenceItemResponse(
                entity.Id,
                entity.DocumentId,
                entity.Status.ToString(),
                entity.CreatedAtUtc,
                entity.UpdatedAtUtc))
            .ToArray();

        return Ok(new GetEvidenceListResponse(
            items,
            result.Page,
            result.PageSize,
            result.TotalCount,
            result.TotalPages));
    }

    [HttpGet("{id:guid}")]
    public async Task<ActionResult<GetEvidenceResponse>> GetEvidenceById(
        [FromRoute] Guid id,
        CancellationToken cancellationToken = default)
    {
        var result = await _evidenceService.GetEvidenceAsync(1, int.MaxValue, null, null, cancellationToken);
        var entity = result.Items.FirstOrDefault(x => x.Id == id);

        if (entity is null)
        {
            return NotFound();
        }

        return Ok(new GetEvidenceResponse(
            entity.Id,
            entity.SourceId,
            entity.DocumentId,
            entity.Type.ToString(),
            entity.Status.ToString(),
            entity.Content,
            entity.ContentHash,
            entity.LanguageCode,
            entity.Span.HasValue ? new GetEvidenceResponseItemSpan(entity.Span.Value.StartOffset, entity.Span.Value.EndOffset) : null,
            entity.CapturedAtUtc,
            entity.CreatedAtUtc,
            entity.UpdatedAtUtc));
    }

    [HttpPost]
    public async Task<ActionResult<CreateEvidenceResponse>> CreateEvidence(
        [FromBody] CreateEvidenceRequest request,
        CancellationToken cancellationToken = default)
    {
        var entity = await _evidenceService.AddEvidenceAsync(
            request.DocumentId,
            request.Quote,
            request.StartOffset,
            request.EndOffset,
            request.Context,
            request.CreatedBy,
            cancellationToken);

        var response = new CreateEvidenceResponse(
            entity.Id,
            entity.DocumentId,
            request.Quote,
            entity.Status.ToString(),
            entity.CreatedAtUtc,
            entity.UpdatedAtUtc);

        return Ok(response);
    }
}
