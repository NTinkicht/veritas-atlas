namespace VeritasAtlas.Infrastructure.Persistence;

/// <summary>
/// One append-only fetch attempt for a tenant source (VER-P0-03 slice 3, #89).
/// <see cref="Outcome"/> is <c>succeeded</c> or <c>failed</c>;
/// <see cref="FailureClass"/> uses the snake_case database values.
/// </summary>
public sealed class TenantFetchAttempt
{
    public Guid TenantId { get; set; }

    public Guid Id { get; set; }

    public Guid SourceEntryId { get; set; }

    public string AttemptKey { get; set; } = string.Empty;

    public string RequestedUri { get; set; } = string.Empty;

    public string Outcome { get; set; } = string.Empty;

    public string? FailureClass { get; set; }

    public int? HttpStatus { get; set; }

    public Guid? RawRetrievalId { get; set; }

    public DateTime AttemptedAtUtc { get; set; }

    public DateTime CreatedAtUtc { get; set; }
}
