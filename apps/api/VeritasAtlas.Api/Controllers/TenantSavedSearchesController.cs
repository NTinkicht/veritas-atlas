using System.Security.Claims;
using Microsoft.AspNetCore.Mvc;
using VeritasAtlas.Api.Infrastructure.Auth;
using VeritasAtlas.Application.Interfaces;
using VeritasAtlas.Domain.Provenance;

namespace VeritasAtlas.Api.Controllers;

/// <summary>
/// VER-P1-01 slice 1: private saved evidence searches. The tenant comes only from the signed
/// <c>tenant</c> claim and the owner only from the name claim, never from the request. Exactly one
/// author role (operator or analyst) is required. Only search criteria are stored; running a saved
/// search goes through the normal evidence search, which re-evaluates freshness at that request.
/// Another subject's or tenant's saved search answers exactly like an unknown id.
/// </summary>
[ApiController]
[Route("api/v1/tenant-saved-searches")]
public sealed class TenantSavedSearchesController : ControllerBase
{
    private static readonly string[] AuthorRoles = ["operator", "analyst"];

    private readonly ITenantSavedSearchStore _store;

    public TenantSavedSearchesController(ITenantSavedSearchStore store)
    {
        _store = store;
    }

    [HttpPost]
    public async Task<IActionResult> Create([FromBody] TenantSavedSearchRequest? body, CancellationToken cancellationToken)
    {
        if (Authorize() is not { } caller) return Refusal!;
        if (Validate(body) is not { } definition)
        {
            return Error(StatusCodes.Status400BadRequest, "invalid_saved_search",
                "The name or a search criterion is invalid.");
        }

        var result = await _store.CreateAsync(caller.TenantId, caller.Subject, definition, cancellationToken);
        return result.Outcome switch
        {
            SavedSearchCreateOutcome.Created => StatusCode(StatusCodes.Status201Created, ToResponse(result.SavedSearch!)),
            SavedSearchCreateOutcome.DuplicateName => Error(StatusCodes.Status409Conflict, "saved_search_name_taken",
                "A saved search with this name already exists."),
            _ => Error(StatusCodes.Status409Conflict, "saved_search_limit_reached",
                $"At most {TenantSavedSearchLimits.MaxPerSubject} saved searches are allowed.")
        };
    }

    [HttpGet]
    public async Task<IActionResult> List(CancellationToken cancellationToken)
    {
        if (Authorize() is not { } caller) return Refusal!;
        var items = await _store.ListAsync(caller.TenantId, caller.Subject, cancellationToken);
        return Ok(new TenantSavedSearchListResponse(items.Select(ToResponse).ToList()));
    }

    [HttpDelete("{id:guid}")]
    public async Task<IActionResult> Delete(Guid id, CancellationToken cancellationToken)
    {
        if (Authorize() is not { } caller) return Refusal!;
        return await _store.DeleteAsync(caller.TenantId, caller.Subject, id, cancellationToken)
            ? NoContent()
            : Error(StatusCodes.Status404NotFound, "saved_search_not_found", "The saved search was not found.");
    }

    private IActionResult? Refusal { get; set; }

    private (Guid TenantId, string Subject)? Authorize()
    {
        if (TenantClaim.GetTenantId(User) is not { } tenantId)
        {
            Refusal = Error(StatusCodes.Status403Forbidden, "tenant_required",
                "The access token carries no valid organization tenant.");
            return null;
        }

        var subject = User.FindFirstValue(ClaimTypes.Name);
        if (string.IsNullOrWhiteSpace(subject)
            || subject.Length > TenantInvestigationLimits.MaxSubjectLength
            || subject.Length > TenantAuditEventLimits.MaxSubjectLength
            || subject.Any(char.IsControl))
        {
            Refusal = Error(StatusCodes.Status403Forbidden, "actor_required",
                "The access token carries no usable subject name.");
            return null;
        }

        var held = User.FindAll(ClaimTypes.Role).Concat(User.FindAll("role"))
            .Select(c => c.Value)
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToList();
        if (held.Count != 1 || !AuthorRoles.Contains(held[0], StringComparer.OrdinalIgnoreCase))
        {
            Refusal = Error(StatusCodes.Status403Forbidden, "author_required",
                "Only an operator or analyst may manage saved searches.");
            return null;
        }

        return (tenantId, subject);
    }

    private static TenantSavedSearchDefinition? Validate(TenantSavedSearchRequest? b)
    {
        if (b is null || b.Name is null || b.Query is null) return null;
        if (b.Name.Length is < 1 or > TenantSavedSearchLimits.MaxNameLength
            || b.Name != b.Name.Trim() || b.Name.Any(char.IsControl)) return null;
        if (string.IsNullOrWhiteSpace(b.Query) || b.Query.Length > EvidenceSearchLimits.MaxQueryLength
            || b.Query.Any(char.IsControl)) return null;
        if (!ValidFilter(b.Jurisdiction) || !ValidFilter(b.SourceType) || !ValidFilter(b.ParserName)
            || !ValidFilter(b.ParserVersion) || !ValidFilter(b.SubjectTerm)) return null;
        if (b.SubjectTerm is not null && string.IsNullOrWhiteSpace(b.SubjectTerm)) return null;
        if (b.SourceEntryId == Guid.Empty) return null;

        string? freshness = null;
        if (b.Freshness is not null)
        {
            if (b.Freshness.Length is < 1 or > 16 || char.IsDigit(b.Freshness[0])
                || !Enum.TryParse<FreshnessState>(b.Freshness, ignoreCase: true, out var state)
                || !Enum.IsDefined(state)) return null;
            freshness = state.ToString();
        }

        return new TenantSavedSearchDefinition(
            b.Name, b.Query, b.SourceEntryId, b.Jurisdiction, b.SourceType, freshness,
            b.ParserName, b.ParserVersion, b.SubjectTerm);
    }

    private static bool ValidFilter(string? value) =>
        value is null || (value.Length is >= 1 and <= EvidenceSearchLimits.MaxFilterLength && !value.Any(char.IsControl));

    private ObjectResult Error(int status, string code, string message) =>
        StatusCode(status, new TenantSavedSearchError(code, message));

    private static TenantSavedSearchResponse ToResponse(TenantSavedSearchModel m) =>
        new(m.Id, m.Definition.Name, m.Definition.Query, m.Definition.SourceEntryId, m.Definition.Jurisdiction,
            m.Definition.SourceType, m.Definition.Freshness, m.Definition.ParserName, m.Definition.ParserVersion,
            m.Definition.SubjectTerm, m.CreatedAtUtc);
}

public sealed record TenantSavedSearchRequest(
    string? Name, string? Query, Guid? SourceEntryId, string? Jurisdiction, string? SourceType,
    string? Freshness, string? ParserName, string? ParserVersion, string? SubjectTerm);

public sealed record TenantSavedSearchResponse(
    Guid Id, string Name, string Query, Guid? SourceEntryId, string? Jurisdiction, string? SourceType,
    string? Freshness, string? ParserName, string? ParserVersion, string? SubjectTerm, DateTime CreatedAtUtc);

public sealed record TenantSavedSearchListResponse(IReadOnlyList<TenantSavedSearchResponse> Items);

public sealed record TenantSavedSearchError(string Code, string Message);
