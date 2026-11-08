namespace VeritasAtlas.Application.Interfaces;

/// <summary>
/// VER-P0-10 slice 4: append-only analyst annotations on evidence items collected into a tenant
/// investigation. Every read and write is scoped to the caller tenant, and another tenant's
/// investigation or item is indistinguishable from an unknown one. Posting the same text by the
/// same author on the same item again is an idempotent replay of the original row.
/// </summary>
public interface ITenantInvestigationAnnotationStore
{
    Task<AnnotationAddResult> AddAsync(
        Guid tenantId, Guid investigationId, Guid evidenceItemId, AnnotationInput input,
        CancellationToken cancellationToken);

    /// <summary>Null when the investigation or item does not exist in the tenant.</summary>
    Task<IReadOnlyList<TenantInvestigationAnnotationModel>?> ListAsync(
        Guid tenantId, Guid investigationId, Guid evidenceItemId, CancellationToken cancellationToken);
}

public sealed record AnnotationInput(string Text, string AuthorSubject);

public sealed record TenantInvestigationAnnotationModel(
    Guid Id,
    Guid InvestigationId,
    Guid EvidenceItemId,
    string Text,
    string AuthorSubject,
    DateTime CreatedAtUtc);

public enum AnnotationAddOutcome
{
    Added,
    Replayed,
    NotFound
}

public sealed record AnnotationAddResult(AnnotationAddOutcome Outcome, TenantInvestigationAnnotationModel? Annotation);

public static class TenantInvestigationAnnotationLimits
{
    public const int MaxTextLength = 1000;

    public static bool IsValidText(string? value) =>
        !string.IsNullOrWhiteSpace(value)
        && value.Length <= MaxTextLength
        && !char.IsWhiteSpace(value[0])
        && !char.IsWhiteSpace(value[^1])
        && !value.Any(c => char.IsControl(c) && c != '\n');
}
