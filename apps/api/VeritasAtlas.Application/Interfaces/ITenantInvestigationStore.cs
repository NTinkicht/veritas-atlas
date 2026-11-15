namespace VeritasAtlas.Application.Interfaces;

/// <summary>
/// VER-P0-03 slice 5 (#93): the minimum tenant-scoped investigation record a
/// later research workspace (VER-P0-10) builds on. A creation record is
/// immutable: it can never be updated or deleted. Reads are filtered to the
/// caller tenant; another tenant's investigation is indistinguishable from an
/// unknown one.
/// </summary>
public interface ITenantInvestigationStore
{
    Task<InvestigationCreateResult> CreateAsync(
        Guid tenantId, InvestigationInput input, CancellationToken cancellationToken);

    Task<TenantInvestigationModel?> GetAsync(
        Guid tenantId, Guid investigationId, CancellationToken cancellationToken);

    Task<IReadOnlyList<TenantInvestigationModel>> ListAsync(
        Guid tenantId, CancellationToken cancellationToken);

    /// <summary>
    /// VER-P0-10 slice 8: appends a close (or reopen) state event. The creation record is never
    /// touched. Optimistic: <paramref name="expectedVersion"/> must equal the current version.
    /// The caller authorizes the actor first.
    /// </summary>
    Task<InvestigationTransitionResult> TransitionAsync(
        Guid tenantId, Guid investigationId, bool close, int expectedVersion, string actorSubject,
        CancellationToken cancellationToken);
}

public enum InvestigationTransitionOutcome
{
    /// <summary>A new state event was appended.</summary>
    Transitioned,

    /// <summary>The same request was already applied: the current state is returned unchanged.</summary>
    Replayed,

    NotFound,

    /// <summary>The expected version is not the current one.</summary>
    StaleVersion,

    /// <summary>The investigation is already in the requested state.</summary>
    NoChange
}

public sealed record InvestigationTransitionResult(
    InvestigationTransitionOutcome Outcome, TenantInvestigationModel? Investigation);

/// <summary>
/// <see cref="Title"/> is stored exactly as given: it must not be blank, must
/// not start or end with whitespace and must not contain control characters.
/// <see cref="CreatedBySubject"/> is the authenticated subject opening it.
/// </summary>
public sealed record InvestigationInput(
    string CreationKey,
    string Title,
    string CreatedBySubject);

public sealed record TenantInvestigationModel(
    Guid Id,
    string CreationKey,
    string Title,
    string CreatedBySubject,
    DateTime CreatedAtUtc,
    string State = TenantInvestigationStates.Open,
    int Version = 0);

public static class TenantInvestigationStates
{
    public const string Open = "open";
    public const string Closed = "closed";
}

public enum InvestigationCreateOutcome
{
    /// <summary>A new immutable investigation record was written.</summary>
    Created,

    /// <summary>The same creation key with the same content: the original row.</summary>
    Replayed,

    /// <summary>The creation key is already bound to a different investigation.</summary>
    IdempotencyConflict
}

public sealed record InvestigationCreateResult(
    InvestigationCreateOutcome Outcome, TenantInvestigationModel? Investigation);

public static class TenantInvestigationLimits
{
    public const int MaxCreationKeyLength = 200;
    public const int MaxTitleLength = 200;
    public const int MaxSubjectLength = 200;

    // Stricter than ck_tenant_investigations_title_shape: refuses any Unicode
    // leading/trailing whitespace, and char.IsControl covers C0, DEL and C1.
    public static bool IsValidTitle(string? value) =>
        !string.IsNullOrWhiteSpace(value)
        && value.Length <= MaxTitleLength
        && !char.IsWhiteSpace(value[0])
        && !char.IsWhiteSpace(value[^1])
        && !value.Any(char.IsControl);

    public static bool IsValidCreationKey(string? value) =>
        !string.IsNullOrWhiteSpace(value)
        && value.Length <= MaxCreationKeyLength
        && !value.Any(char.IsControl);
}
