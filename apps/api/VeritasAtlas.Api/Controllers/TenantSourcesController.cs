using Microsoft.AspNetCore.Mvc;
using VeritasAtlas.Api.Infrastructure.Auth;
using VeritasAtlas.Application.Interfaces;

namespace VeritasAtlas.Api.Controllers;

/// <summary>
/// VER-P0-03 slice 1 (#82, owner decision A in #80): tenant-scoped source
/// registry. The tenant comes only from the signed <c>tenant</c> claim, never
/// from the request; a caller without a valid tenant claim is refused (403),
/// and another tenant's id answers exactly like an unknown id (404).
/// </summary>
[ApiController]
[Route("api/v1/tenant-sources")]
public sealed class TenantSourcesController : ControllerBase
{
    private readonly ITenantSourceRegistry _registry;

    public TenantSourcesController(ITenantSourceRegistry registry)
    {
        _registry = registry;
    }

    [HttpPost]
    public async Task<IActionResult> Create(
        [FromBody] CreateTenantSourceRequest? request,
        CancellationToken cancellationToken)
    {
        if (TenantClaim.GetTenantId(User) is not { } tenantId) return TenantRequired();

        var canonicalKey = request?.CanonicalKey?.Trim();
        if (string.IsNullOrEmpty(canonicalKey)
            || canonicalKey.Length > TenantSourceRegistryLimits.MaxCanonicalKeyLength
            || canonicalKey.Any(char.IsControl))
        {
            return BadRequest(new TenantSourceError(
                "invalid_canonical_key",
                $"canonicalKey must be 1-{TenantSourceRegistryLimits.MaxCanonicalKeyLength} characters without control characters."));
        }

        var created = await _registry.CreateAsync(tenantId, canonicalKey, cancellationToken);
        if (created is null)
        {
            return Conflict(new TenantSourceError(
                "duplicate_canonical_key",
                "A source with this canonical key is already registered."));
        }

        return CreatedAtAction(nameof(GetById), new { id = created.Id }, ToResponse(created));
    }

    [HttpGet("{id:guid}")]
    public async Task<IActionResult> GetById(Guid id, CancellationToken cancellationToken)
    {
        if (TenantClaim.GetTenantId(User) is not { } tenantId) return TenantRequired();

        var entry = await _registry.GetAsync(tenantId, id, cancellationToken);
        // Anti-oracle: an id owned by another tenant is answered exactly like
        // an id that does not exist.
        return entry is null
            ? NotFound(new TenantSourceError("tenant_source_not_found", "The source was not found."))
            : Ok(ToResponse(entry));
    }

    [HttpGet]
    public async Task<IActionResult> List(CancellationToken cancellationToken)
    {
        if (TenantClaim.GetTenantId(User) is not { } tenantId) return TenantRequired();

        var entries = await _registry.ListAsync(tenantId, cancellationToken);
        return Ok(new TenantSourceListResponse(entries.Select(ToResponse).ToList()));
    }

    private ObjectResult TenantRequired() =>
        StatusCode(StatusCodes.Status403Forbidden, new TenantSourceError(
            "tenant_required",
            "The access token carries no valid organization tenant."));

    private static TenantSourceResponse ToResponse(TenantSourceRegistryEntryModel entry) =>
        new(entry.Id, entry.CanonicalKey, entry.CreatedAtUtc);
}

public sealed record CreateTenantSourceRequest(string? CanonicalKey);

public sealed record TenantSourceResponse(Guid Id, string CanonicalKey, DateTime CreatedAtUtc);

public sealed record TenantSourceListResponse(IReadOnlyList<TenantSourceResponse> Items);

public sealed record TenantSourceError(string Code, string Message);
