namespace VeritasAtlas.Infrastructure.Persistence;

/// <summary>
/// One editorial comment on a publication revision (VER-P1-02 slice 2). Append-only: the schema refuses
/// any update, delete or truncate, and refuses a row whose revision is not of the same tenant.
/// </summary>
public sealed class TenantPublicationComment
{
    public Guid TenantId { get; set; }

    public Guid Id { get; set; }

    public Guid RevisionId { get; set; }

    /// <summary>Caller-chosen idempotency key, unique per (tenant, revision).</summary>
    public Guid ClientKey { get; set; }

    public string AuthorSubject { get; set; } = string.Empty;

    public string Body { get; set; } = string.Empty;

    public DateTime CreatedAtUtc { get; set; }
}
