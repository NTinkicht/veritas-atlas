using System.Security.Claims;
using Microsoft.AspNetCore.Mvc;
using VeritasAtlas.Api.Infrastructure.Auth;
using VeritasAtlas.Domain.Provenance;

namespace VeritasAtlas.Api.Controllers;

public sealed record ClaimConfidenceItemBody(
    string? ClaimId, int? SupportingEvidenceCount, int? IndependentSourceCount, int? ConflictingCount,
    int? SupersededCount, int? StaleCount, int? UnknownFreshnessCount, int? UnresolvedRelationCount);

public sealed record ClaimConfidenceBody(IReadOnlyList<ClaimConfidenceItemBody?>? Claims);

public sealed record ClaimConfidenceLabelView(string ClaimId, string Band, IReadOnlyList<string> Reasons);

public sealed record ClaimConfidenceResponse(bool EvidenceVerified, IReadOnlyList<ClaimConfidenceLabelView> Labels);

public sealed record ClaimConfidenceError(string Code, string Message);

/// <summary>
/// VER-P0-14 slice 2 (#218): stateless HTTP view over the deterministic confidence labeler. A signed
/// <c>tenant</c> claim and exactly one known workflow role are required; nothing is read from or written to
/// storage and the tenant is not part of the request contract. Omitted counts stay null so the labeler
/// reports <c>insufficient</c> instead of defaulting them. The response carries a coarse band and reasons
/// only (no numeric score) and never claims evidence verification or truth.
/// </summary>
[ApiController]
[Route("api/v1/tenant-claims")]
public sealed class TenantClaimConfidenceController : ControllerBase
{
    public const int MaxClaims = 200;
    public const int MaxIdLength = 500;
    public const int MaxCount = 100_000;
    private static readonly string[] ReaderRoles = ["operator", "analyst", "reviewer", "publisher", "admin"];

    [HttpPost("confidence")]
    public IActionResult Label([FromBody] ClaimConfidenceBody? body)
    {
        if (TenantClaim.GetTenantId(User) is null)
            return Error(StatusCodes.Status403Forbidden, "tenant_required", "The access token carries no valid organization tenant.");
        if (!HasExactlyOneRoleIn(User, ReaderRoles))
            return Error(StatusCodes.Status403Forbidden, "role_required", "Exactly one known workflow role is required.");

        var items = body?.Claims;
        if (items is null || items.Count == 0 || items.Count > MaxClaims
            || items.Any(c => c is null || !Valid(c))
            || items.Select(c => c!.ClaimId).Distinct(StringComparer.Ordinal).Count() != items.Count)
        {
            return Error(StatusCodes.Status400BadRequest, "invalid_confidence_request",
                $"claims must hold 1-{MaxClaims} items with unique bounded claim ids and counts between 0 and {MaxCount}.");
        }

        var labels = items.Select(c =>
        {
            var label = ConfidenceLabeler.Label(new ConfidenceInputs(
                c!.SupportingEvidenceCount, c.IndependentSourceCount, c.ConflictingCount, c.SupersededCount,
                c.StaleCount, c.UnknownFreshnessCount, c.UnresolvedRelationCount));
            return new ClaimConfidenceLabelView(c.ClaimId!, label.Band.ToString().ToLowerInvariant(), label.Reasons);
        }).ToList();
        return Ok(new ClaimConfidenceResponse(false, labels));
    }

    private static bool Valid(ClaimConfidenceItemBody c) =>
        !string.IsNullOrWhiteSpace(c.ClaimId) && c.ClaimId.Length <= MaxIdLength && !c.ClaimId.Any(char.IsControl)
        && new[] { c.SupportingEvidenceCount, c.IndependentSourceCount, c.ConflictingCount, c.SupersededCount,
                   c.StaleCount, c.UnknownFreshnessCount, c.UnresolvedRelationCount }
            .All(v => v is null or >= 0 and <= MaxCount);

    private static bool HasExactlyOneRoleIn(ClaimsPrincipal user, string[] allowed)
    {
        var roles = user.FindAll(ClaimTypes.Role).Concat(user.FindAll("role"))
            .Select(c => c.Value)
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToList();
        return roles.Count == 1 && allowed.Contains(roles[0], StringComparer.OrdinalIgnoreCase);
    }

    private ObjectResult Error(int status, string code, string message) =>
        StatusCode(status, new ClaimConfidenceError(code, message));
}
