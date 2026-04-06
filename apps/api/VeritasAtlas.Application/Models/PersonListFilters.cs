namespace VeritasAtlas.Application.Models;

public sealed class PersonListFilters
{
    public int Page { get; init; } = 1;
    public int PageSize { get; init; } = 20;
    public string? Search { get; init; }
    public string SortBy { get; init; } = "CreatedAt";
    public string SortDirection { get; init; } = "desc";
}
