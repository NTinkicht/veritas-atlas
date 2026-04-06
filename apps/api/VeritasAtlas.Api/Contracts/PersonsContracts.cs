namespace VeritasAtlas.Api.Contracts.Persons;

public sealed record GetPersonResponse(
    Guid Id,
    string DisplayName,
    string? Description,
    string? Nationality,
    DateTime CreatedAtUtc,
    DateTime UpdatedAtUtc,
    IReadOnlyCollection<string> Aliases);

public sealed record CreatePersonRequest(
    string DisplayName,
    string? CreatedBy);

public sealed record CreatePersonResponse(
    Guid Id,
    string DisplayName,
    DateTime CreatedAtUtc);

public sealed record AddAliasRequest(
    string Alias,
    string? CreatedBy);

public sealed record AddAliasResponse(
    Guid Id,
    Guid PersonId,
    string Alias,
    DateTime CreatedAtUtc);

public sealed record GetPersonsResponse(
    IReadOnlyCollection<GetPersonResponse> Items,
    int Page,
    int PageSize,
    int TotalCount,
    int TotalPages);
