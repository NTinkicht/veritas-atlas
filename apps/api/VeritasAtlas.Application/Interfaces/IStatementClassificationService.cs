using VeritasAtlas.Domain.Parsing;

namespace VeritasAtlas.Application.Interfaces;

public enum StatementClassificationOutcome
{
    Ok,

    /// <summary>The evidence reference or the statement is malformed.</summary>
    InvalidRequest,

    /// <summary>
    /// No verified evidence with this reference exists for the caller's tenant. Unverified evidence,
    /// unknown references and other tenants' references are deliberately indistinguishable.
    /// </summary>
    NotFound,

    /// <summary>Verified chunks failed integrity checks; nothing is returned (fail closed).</summary>
    Refused
}

public sealed record StatementClassificationServiceResult(
    StatementClassificationOutcome Outcome,
    StatementClassification? Classification,
    ClaimCandidateFreshness? Freshness);

/// <summary>
/// VER-P0-11 slice 4: read-only, tenant-scoped classification of ONE proposed statement against the verified
/// chunks of ONE evidence item. Nothing is persisted and the result never makes a claim answerable.
/// </summary>
public interface IStatementClassificationService
{
    Task<StatementClassificationServiceResult> ClassifyAsync(
        Guid tenantId, string evidenceRef, string statement, CancellationToken cancellationToken);
}
