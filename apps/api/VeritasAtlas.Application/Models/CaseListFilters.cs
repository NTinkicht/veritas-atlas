namespace VeritasAtlas.Application.Models;

public sealed class CaseListFilters
{
    public int Page { get; init; } = 1;
    public int PageSize { get; init; } = 20;
    public string? Status { get; init; }
    public string? Search { get; init; }
    public string SortBy { get; init; } = "CreatedAt";
    public string SortDirection { get; init; } = "desc";
}
