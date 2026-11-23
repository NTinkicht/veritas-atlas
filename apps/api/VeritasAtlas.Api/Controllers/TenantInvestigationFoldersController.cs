using System.Security.Claims;
using Microsoft.AspNetCore.Mvc;
using VeritasAtlas.Api.Infrastructure.Auth;
using VeritasAtlas.Application.Interfaces;

namespace VeritasAtlas.Api.Controllers;

/// <summary>
/// VER-P1-01 slice 3: a private folder for a tenant investigation. The tenant comes only from the
/// signed <c>tenant</c> claim and the folder owner only from the name claim. Filing requires exactly
/// one author role (operator or analyst); reading the caller's own folder requires exactly one known
/// workflow role. Every refusal happens before the store is touched, and another tenant's or an
/// unshared investigation answers exactly like an unknown id. Folders are never shared.
/// </summary>
[ApiController]
[Route("api/v1/tenant-investigations/{investigationId:guid}/folder")]
public sealed class TenantInvestigationFoldersController : ControllerBase
{
    private static readonly string[] AuthorRoles = ["operator", "analyst"];
    private static readonly string[] ReaderRoles = ["operator", "analyst", "reviewer", "publisher", "admin"];

    private readonly ITenantInvestigationFolderStore _store;
    private readonly ITenantInvestigationShareStore _shares;

    public TenantInvestigationFoldersController(ITenantInvestigationFolderStore store, ITenantInvestigationShareStore shares)
    {
        _store = store;
        _shares = shares;
    }

    [HttpPut]
    public async Task<IActionResult> Put(Guid investigationId, [FromBody] TenantInvestigationFolderRequest? request, CancellationToken cancellationToken)
    {
        if (Authorize(AuthorRoles, "author_required", "Only an operator or analyst may file an investigation.")
            is not { } caller) return Refusal!;
        if (TenantInvestigationFolderRules.Normalize(request?.Name) is not { } name)
        {
            return Error(StatusCodes.Status400BadRequest, "invalid_folder_name",
                "A folder name is 1 to 60 characters: letters, digits, single spaces, '_', '.' or '-'.");
        }

        if (!await _shares.IsAuthorizedAsync(caller.TenantId, investigationId, caller.Subject, cancellationToken))
        {
            return NotFoundError();
        }

        var result = await _store.SetAsync(caller.TenantId, investigationId, name, caller.Subject, cancellationToken);
        return result.Outcome switch
        {
            FolderSetOutcome.Created => StatusCode(StatusCodes.Status201Created, ToResponse(result.Folder!)),
            FolderSetOutcome.Changed or FolderSetOutcome.Replayed => Ok(ToResponse(result.Folder!)),
            FolderSetOutcome.InvestigationClosed => ClosedError(),
            _ => NotFoundError()
        };
    }

    [HttpDelete]
    public async Task<IActionResult> Clear(Guid investigationId, CancellationToken cancellationToken)
    {
        if (Authorize(AuthorRoles, "author_required", "Only an operator or analyst may clear a folder.")
            is not { } caller) return Refusal!;
        if (!await _shares.IsAuthorizedAsync(caller.TenantId, investigationId, caller.Subject, cancellationToken))
        {
            return NotFoundError();
        }

        var outcome = await _store.ClearAsync(caller.TenantId, investigationId, caller.Subject, cancellationToken);
        return outcome switch
        {
            FolderClearOutcome.NotFound => NotFoundError(),
            FolderClearOutcome.InvestigationClosed => ClosedError(),
            _ => NoContent()
        };
    }

    [HttpGet]
    public async Task<IActionResult> Get(Guid investigationId, CancellationToken cancellationToken)
    {
        if (Authorize(ReaderRoles, "role_not_permitted", "The caller's role may not read a folder.")
            is not { } caller) return Refusal!;
        if (!await _shares.IsAuthorizedAsync(caller.TenantId, investigationId, caller.Subject, cancellationToken))
        {
            return NotFoundError();
        }

        var result = await _store.GetAsync(caller.TenantId, investigationId, caller.Subject, cancellationToken);
        if (!result.InvestigationFound) return NotFoundError();
        return result.Folder is null
            ? Error(StatusCodes.Status404NotFound, "investigation_folder_not_set", "The investigation is not filed in a folder.")
            : Ok(ToResponse(result.Folder));
    }

    private IActionResult? Refusal { get; set; }

    private (Guid TenantId, string Subject)? Authorize(string[] roles, string roleCode, string roleMessage)
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
        if (held.Count != 1 || !roles.Contains(held[0], StringComparer.OrdinalIgnoreCase))
        {
            Refusal = Error(StatusCodes.Status403Forbidden, roleCode, roleMessage);
            return null;
        }

        return (tenantId, subject);
    }

    private ObjectResult ClosedError() =>
        Error(StatusCodes.Status409Conflict, "investigation_closed", "The investigation is closed; reopen it to change it.");

    private ObjectResult NotFoundError() =>
        Error(StatusCodes.Status404NotFound, "tenant_investigation_not_found", "The investigation was not found.");

    private ObjectResult Error(int status, string code, string message) =>
        StatusCode(status, new TenantInvestigationFolderError(code, message));

    private static TenantInvestigationFolderResponse ToResponse(TenantInvestigationFolderModel m) =>
        new(m.InvestigationId, m.Name, m.UpdatedAtUtc);
}

public sealed record TenantInvestigationFolderRequest(string? Name);

public sealed record TenantInvestigationFolderResponse(Guid InvestigationId, string Name, DateTime UpdatedAtUtc);

public sealed record TenantInvestigationFolderError(string Code, string Message);
