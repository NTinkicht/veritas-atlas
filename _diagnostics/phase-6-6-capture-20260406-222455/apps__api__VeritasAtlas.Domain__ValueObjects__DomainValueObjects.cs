namespace VeritasAtlas.Domain.ValueObjects
{
    public sealed record PersonName(
        string GivenName,
        string? MiddleName,
        string FamilyName,
        string DisplayName);

    public sealed record SourceReference(
        string? ExternalId,
        string? Url,
        string? Domain,
        string? LanguageCode);

    public sealed record StatementText(
        string Raw,
        string Normalized);

    public readonly record struct DocumentSpan(
        int StartOffset,
        int EndOffset);

    public readonly record struct ConfidenceValue(
        decimal Value);
}
