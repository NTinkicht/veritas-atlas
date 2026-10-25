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
}

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
    DateTime CreatedAtUtc);

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
}
