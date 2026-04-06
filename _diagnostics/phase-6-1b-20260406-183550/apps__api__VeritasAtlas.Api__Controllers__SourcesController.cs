using Microsoft.AspNetCore.Mvc;
using VeritasAtlas.Api.Contracts.Sources;
using VeritasAtlas.Application.Interfaces;
using VeritasAtlas.Domain.Enums;

namespace VeritasAtlas.Api.Controllers;

[ApiController]
[Route("api/v1/sources")]
public sealed class SourcesController : ControllerBase
{
    private readonly ISourceService _sourceService;

    public SourcesController(ISourceService sourceService)
    {
        _sourceService = sourceService;
    }

    [HttpGet]
    public async Task<ActionResult<GetSourcesResponse>> GetSources(
        [FromQuery] int page = 1,
        [FromQuery] int pageSize = 20,
        CancellationToken cancellationToken = default)
    {
        var result = await _sourceService.GetSourcesAsync(page, pageSize, cancellationToken);

        var items = result.Items
            .Select(entity => new GetSourcesItemResponse(
                entity.Id,
                entity.Name,
                entity.Type.ToString(),
                entity.Reference?.ToString(),
                entity.Status.ToString(),
                entity.CreatedAtUtc,
                entity.UpdatedAtUtc))
            .ToArray();

        return Ok(new GetSourcesResponse(
            items,
            result.Page,
            result.PageSize,
            result.TotalCount,
            result.TotalPages));
    }

    [HttpPost]
    public async Task<ActionResult<CreateSourceResponse>> CreateSource(
        [FromBody] CreateSourceRequest request,
        CancellationToken cancellationToken = default)
    {
        if (!Enum.TryParse<SourceType>(request.Type, true, out var sourceType))
        {
            return BadRequest(new { message = "Invalid source type." });
        }

        var entity = await _sourceService.RegisterSourceAsync(
            request.Name,
            sourceType,
            request.Reference ?? string.Empty,
            request.CreatedBy,
            cancellationToken);

        var response = new CreateSourceResponse(
            entity.Id,
            entity.Name,
            entity.Type.ToString(),
            entity.Reference?.ToString(),
            entity.Status.ToString(),
            entity.CreatedAtUtc,
            entity.UpdatedAtUtc);

        return Ok(response);
    }
}
