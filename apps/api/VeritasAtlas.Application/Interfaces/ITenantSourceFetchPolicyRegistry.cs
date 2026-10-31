using VeritasAtlas.Domain.Provenance;

namespace VeritasAtlas.Application.Interfaces;

/// <summary>
/// VER-P0-04 slice 5b (#121): tenant-scoped fetch policy revisions of a
/// tenant source registry entry. A first policy must pass
/// <see cref="SourceFetchPolicyAdmission"/>; every later one must pass
/// <see cref="SourceFetchPolicyChange"/> against the current revision and every
/// version the source ever used. Another tenant's source is indistinguishable
/// from a missing one.
/// </summary>
public interface ITenantSourceFetchPolicyRegistry
{
    /// <summary>Evaluates <paramref name="proposed"/> and, when admitted and
    /// different, appends the next revision together with its audit event
    /// (actor <paramref name="actorSubject"/>) in one transaction.
    /// <paramref name="wideningApproved"/> is the caller's explicit approval;
    /// who may grant it is decided by the caller, not here.</summary>
    Task<SourceFetchPolicyProposalResult> ProposeAsync(
        Guid tenantId,
        Guid sourceRegistryEntryId,
        SourceFetchPolicy proposed,
        bool wideningApproved,
        string actorSubject,
        CancellationToken cancellationToken);

    /// <summary>The source's highest revision, or null when the source has no
    /// policy, does not exist or belongs to another tenant.</summary>
    Task<TenantSourceFetchPolicyModel?> GetCurrentAsync(
        Guid tenantId, Guid sourceRegistryEntryId, CancellationToken cancellationToken);
}

public enum SourceFetchPolicyProposalOutcome
{
    /// <summary>A new revision was written (see <see cref="SourceFetchPolicyProposalResult.Policy"/>).</summary>
    Created,

    /// <summary>The identical policy is already current: nothing written.</summary>
    Unchanged,

    /// <summary>Admission or change policy refused it: nothing written.</summary>
    Refused,

    /// <summary>No such source for this tenant: nothing written.</summary>
    SourceNotFound,

    /// <summary>Another writer committed the same revision first: nothing written.</summary>
    Conflict
}

public sealed record SourceFetchPolicyProposalResult(
    SourceFetchPolicyProposalOutcome Outcome,
    TenantSourceFetchPolicyModel? Policy,
    IReadOnlyList<string> Reasons);

public sealed record TenantSourceFetchPolicyModel(
    Guid Id,
    Guid SourceRegistryEntryId,
    int Revision,
    SourceFetchPolicy Policy,
    bool WideningApproved,
    DateTime CreatedAtUtc);
