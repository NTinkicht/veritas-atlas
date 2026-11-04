using VeritasAtlas.Domain.Provenance;

namespace VeritasAtlas.Application.Interfaces;

/// <summary>
/// VER-P0-16 slice 4 (#196): tenant-scoped durable publication revisions. Legality is decided by the pure
/// <see cref="PublicationLifecycle"/> and optimistic-version/idempotency/approval binding by <see cref="PublicationVersionGuard"/>;
/// this store only supplies their inputs from the database and applies the decision atomically with an audit row.
/// A revision of another tenant is indistinguishable from an unknown one.
/// </summary>
public interface ITenantPublicationRevisionStore
{
    /// <summary>Creates a Draft at version 0. The content digest is immutable; a content change is a new revision.</summary>
    Task<TenantPublicationRevisionModel> CreateDraftAsync(
        Guid tenantId, string authorId, string contentDigest, bool policyPermitsPublicDisplay, CancellationToken cancellationToken);

    Task<TenantPublicationRevisionModel?> GetAsync(Guid tenantId, Guid revisionId, CancellationToken cancellationToken);

    Task<PublicationTransitionResult> TransitionAsync(
        Guid tenantId, Guid revisionId, string actorId, LifecycleRole actorRole, LifecycleAction action,
        long expectedVersion, string operationId, CancellationToken cancellationToken);
}

public sealed record TenantPublicationRevisionModel(
    Guid Id, string AuthorId, string ContentDigest, string? ApprovedDigest, bool PolicyPermitsPublicDisplay,
    LifecycleState State, long Version);

public enum PublicationTransitionOutcome { Applied, Replayed, Denied, NotFound, ConcurrentUpdate }

public sealed record PublicationTransitionResult(
    PublicationTransitionOutcome Outcome, string? DenialCode, TenantPublicationRevisionModel? Revision);
