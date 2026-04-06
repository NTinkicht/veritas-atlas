)
        .FirstOrDefaultAsync();

    public async Task<(int Total, List<StatementListItemResponse> Items)> GetStatementsAsync(int page, int pageSize)
    {
        var query = _dbContext.Statements.AsNoTracking();

        var total = await query.CountAsync();

        var items = await query
            .OrderByDescending(x => x.CreatedAt)
            .Skip((page - 1) * pageSize)
            .Take(pageSize)
            .Select(x => new StatementListItemResponse
            {
                Id = x.Id,
                Text = x.Text.Value,
                Topic = x.Topic,
                Predicate = x.Predicate,
                Object = x.Object,
                Polarity = x.Polarity.ToString(),
                Status = x.Status.ToString(),
                CreatedAt = x.CreatedAt
            })
            .ToListAsync();

        return (total, items);
    }

    public async Task<StatementDetailResponse?> GetStatementByIdAsync(Guid id)
    {
        return await _dbContext.Statements
            .AsNoTracking()
            .Where(x => x.Id == id)
            .Select(x => new StatementDetailResponse
            {
                Id = x.Id,
                Text = x.Text.Value,
                Topic = x.Topic,
                Predicate = x.Predicate,
                Object = x.Object,
                Polarity = x.Polarity.ToString(),
                Status = x.Status.ToString(),
                EvidenceId = x.EvidenceId,
                PersonId = x.PersonId,
                CreatedAt = x.CreatedAt
            })
            .FirstOrDefaultAsync();
    }

}
