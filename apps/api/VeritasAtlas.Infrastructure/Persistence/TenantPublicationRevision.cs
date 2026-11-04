namespace VeritasAtlas.Infrastructure.Persistence;

/// <summary>
/// One tenant-scoped publication revision (VER-P0-16 slice 4, #196). Identity, author, content digest and the public-display
/// permit are immutable; only the lifecycle state, version and last-operation markers move, one version step at a time.
/// </summary>
public sealed class TenantPublicationRevision
{
    public Guid TenantId { get; set; }

    public Guid Id { get; set; }

    public string AuthorId { get; set; } = string.Empty;

    public string ContentDigest { get; set; } = string.Empty;

    /// <summary>The content digest bound by the last Approve; cleared whenever the revision leaves Approved/Published for Draft.</summary>
    public string? ApprovedDigest { get; set; }

    public bool PolicyPermitsPublicDisplay { get; set; }

    public string State { get; set; } = string.Empty;

    public long Version { get; set; }

    public string? LastOperationId { get; set; }

    public string? LastAction { get; set; }
}

/// <summary>Append-only audit ledger row: one per applied transition of a <see cref="TenantPublicationRevision"/>.</summary>
public sealed class TenantPublicationTransition
{
    public Guid TenantId { get; set; }

    public Guid RevisionId { get; set; }

    /// <summary>The revision version this transition produced.</summary>
    public long Version { get; set; }

    public string OperationId { get; set; } = string.Empty;

    public string Action { get; set; } = string.Empty;

    public string FromState { get; set; } = string.Empty;

    public string ToState { get; set; } = string.Empty;

    public string ActorId { get; set; } = string.Empty;

    public string ActorRole { get; set; } = string.Empty;

    public DateTime OccurredAtUtc { get; set; }
}
