namespace VeritasAtlas.Api.Contracts.Statements;

public sealed record CreateStatementRequest(
    Guid EvidenceId,
    string Text,
    string? CreatedBy);

public sealed record CreateStatementResponse(
    Guid Id,
    Guid EvidenceId,
    Guid? DocumentId,
    string Text,
    string Polarity,
    string Status,
    string? Topic,
    DateTime CreatedAtUtc,
    DateTime UpdatedAtUtc);
