using System;

namespace VeritasAtlas.Application.Contracts.Statements;

public class StatementListItemResponse
{
    public Guid Id { get; set; }
    public string Text { get; set; } = default!;
    public string? Topic { get; set; }
    public string? Predicate { get; set; }
    public string? Object { get; set; }
    public string Polarity { get; set; } = default!;
    public string Status { get; set; } = default!;
    public DateTime CreatedAt { get; set; }
}

public class StatementDetailResponse
{
    public Guid Id { get; set; }
    public string Text { get; set; } = default!;
    public string? Topic { get; set; }
    public string? Predicate { get; set; }
    public string? Object { get; set; }
    public string Polarity { get; set; } = default!;
    public string Status { get; set; } = default!;
    public Guid? EvidenceId { get; set; }
    public Guid? PersonId { get; set; }
    public DateTime CreatedAt { get; set; }
}

public class StatementListRequest
{
    public int Page { get; set; } = 1;
    public int PageSize { get; set; } = 20;
}
