using System.Security.Claims;
using Microsoft.AspNetCore.Mvc;
using VeritasAtlas.Api.Infrastructure.Auth;
using VeritasAtlas.Domain.Provenance;

namespace VeritasAtlas.Api.Controllers;

public sealed record ClaimRelationClaimBody(
    string? ClaimId, string? EvidenceRef, string? SourceId, string? Subject, string? EntityKey,
    string? Predicate, bool Negated, string? Value, DateOnly? AsOf);

public sealed record ClaimRelationGraphBody(IReadOnlyList<ClaimRelationClaimBody?>? Claims);

public sealed record ClaimRelationGraphResponse(bool EvidenceVerified, ClaimRelationGraph Graph);

public sealed record ClaimRelationGraphError(string Code, string Message);

/// <summary>
/// VER-P0-13 slice 3 (#216): stateless HTTP view over the deterministic claim relation graph. The tenant
/// comes only from the signed <c>tenant</c> claim and is stamped onto every claim by the server; a caller
/// supplied tenant is not part of the contract. Exactly one known workflow role is required. Nothing is
/// read from or written to storage and evidence references are echoed, not verified
/// (<c>evidenceVerified</c> is always false). Every refusal happens before the builder runs.
/// </summary>
[ApiController]
[Route("api/v1/tenant-claims")]
public sealed class TenantClaimRelationGraphController : ControllerBase
{
    public const int MaxFieldLength = 500;
    private static readonly string[] ReaderRoles = ["operator", "analyst", "reviewer", "publisher", "admin"];

    [HttpPost("relation-graph")]
    public IActionResult Build([FromBody] ClaimRelationGraphBody? body)
    {
        if (TenantClaim.GetTenantId(User) is not { } tenantId)
            return Error(StatusCodes.Status403Forbidden, "tenant_required", "The access token carries no valid organization tenant.");
        if (!HasExactlyOneRoleIn(User, ReaderRoles))
            return Error(StatusCodes.Status403Forbidden, "role_required", "Exactly one known workflow role is required.");

        var items = body?.Claims;
        if (items is null || items.Count == 0 || items.Count > ClaimRelationGraphBuilder.MaxClaims
            || items.Any(c => c is null || !Valid(c)))
        {
            return Error(StatusCodes.Status400BadRequest, "invalid_claim_graph_request",
                $"claims must hold 1-{ClaimRelationGraphBuilder.MaxClaims} claims with bounded, control-free fields.");
        }

        var tenant = tenantId.ToString("D");
        var claims = items.Select(c => new StructuredClaim(
            tenant, c!.ClaimId!, c.EvidenceRef!, c.SourceId, c.Subject!, c.EntityKey, c.Predicate!,
            c.Negated, c.Value, c.AsOf)).ToList();
        try
        {
            return Ok(new ClaimRelationGraphResponse(false, ClaimRelationGraphBuilder.Build(claims)));
        }
        catch (ArgumentException)
        {
            return Error(StatusCodes.Status400BadRequest, "invalid_claim_graph_request", "The claim set is invalid (for example duplicate claim ids).");
        }
    }

    private static bool Valid(ClaimRelationClaimBody c) =>
        Required(c.ClaimId) && Required(c.EvidenceRef) && Required(c.Subject) && Required(c.Predicate)
        && Optional(c.SourceId) && Optional(c.EntityKey) && Optional(c.Value);

    private static bool Required(string? s) => !string.IsNullOrWhiteSpace(s) && Optional(s);

    private static bool Optional(string? s) => s is null || (s.Length <= MaxFieldLength && !s.Any(char.IsControl));

    private static bool HasExactlyOneRoleIn(ClaimsPrincipal user, string[] allowed)
    {
        var roles = user.FindAll(ClaimTypes.Role).Concat(user.FindAll("role"))
            .Select(c => c.Value)
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToList();
        return roles.Count == 1 && allowed.Contains(roles[0], StringComparer.OrdinalIgnoreCase);
    }

    private ObjectResult Error(int status, string code, string message) =>
        StatusCode(status, new ClaimRelationGraphError(code, message));
}
