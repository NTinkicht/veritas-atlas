namespace VeritasAtlas.Infrastructure.Persistence;

/// <summary>
/// One append-only parse run over a tenant raw retrieval (VER-P0-03 slice 4,
/// #91). <see cref="Outcome"/> is <c>failed</c> or <c>parsed_unverified</c>;
/// <see cref="FailureClass"/> uses the snake_case database values.
/// <see cref="EvidenceReference"/> is the nullable unverified slot: the schema
/// refuses any non-null value until VER-P0-07's versioned parser migration.
/// </summary>
public sealed class TenantParseRun
{
    public Guid TenantId { get; set; }

    public Guid Id { get; set; }

    public Guid RawRetrievalId { get; set; }

    public string RunKey { get; set; } = string.Empty;

    public string ParserName { get; set; } = string.Empty;

    public string ParserVersion { get; set; } = string.Empty;

    public string Outcome { get; set; } = string.Empty;

    public string? FailureClass { get; set; }

    public string? EvidenceReference { get; set; }

    public DateTime StartedAtUtc { get; set; }

    public DateTime CreatedAtUtc { get; set; }
}
