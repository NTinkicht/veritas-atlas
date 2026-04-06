using Microsoft.EntityFrameworkCore;
using VeritasAtlas.Application.Common;
using VeritasAtlas.Application.Interfaces;
using VeritasAtlas.Application.Models;
using VeritasAtlas.Domain.Entities;
using VeritasAtlas.Domain.ValueObjects;
using VeritasAtlas.Infrastructure.Persistence;

namespace VeritasAtlas.Infrastructure.Services;

public sealed class PersonService : IPersonService
{
    private readonly VeritasAtlasDbContext _dbContext;

    public PersonService(VeritasAtlasDbContext dbContext)
    {
        _dbContext = dbContext;
    }

    public async Task<PagedResult<Person>> GetPersonsAsync(PersonListFilters filters, CancellationToken cancellationToken = default)
    {
        var query = _dbContext.Persons
            .Include(x => x.Aliases)
            .AsNoTracking()
            .AsQueryable();

        if (!string.IsNullOrWhiteSpace(filters.Search))
        {
            var search = filters.Search.Trim();
            query = query.Where(x =>
                x.Name.DisplayName.Contains(search) ||
                x.Aliases.Any(a => a.Value.Contains(search)));
        }

        query = filters.SortBy.ToLowerInvariant() switch
        {
            "createdat" => filters.SortDirection.Equals("asc", StringComparison.OrdinalIgnoreCase)
                ? query.OrderBy(x => x.CreatedAtUtc)
                : query.OrderByDescending(x => x.CreatedAtUtc),

            _ => query.OrderByDescending(x => x.CreatedAtUtc)
        };

        var totalCount = await query.CountAsync(cancellationToken);
        var items = await query
            .Skip((filters.Page - 1) * filters.PageSize)
            .Take(filters.PageSize)
            .ToListAsync(cancellationToken);

        return new PagedResult<Person>
        {
            Items = items,
            Page = filters.Page,
            PageSize = filters.PageSize,
            TotalCount = totalCount,
            TotalPages = (int)Math.Ceiling(totalCount / (double)filters.PageSize)
        };
    }

    public async Task<Person> GetPersonByIdAsync(Guid id, CancellationToken cancellationToken = default)
    {
        var entity = await _dbContext.Persons
            .Include(x => x.Aliases)
            .FirstOrDefaultAsync(x => x.Id == id, cancellationToken);

        if (entity is null)
        {
            throw new NotFoundException($"Person '{id}' was not found.");
        }

        return entity;
    }

    public async Task<Person> CreatePersonAsync(string displayName, string? createdBy = null, CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(displayName))
        {
            throw new ValidationException("Display name is required.");
        }

        var entity = new Person
        {
            Name = new PersonName(displayName, null, displayName, displayName),
            Description = createdBy
        };

        _dbContext.Persons.Add(entity);
        await _dbContext.SaveChangesAsync(cancellationToken);

        return entity;
    }

    public async Task<Alias> AddAliasAsync(Guid personId, string alias, string? createdBy = null, CancellationToken cancellationToken = default)
    {
        var person = await _dbContext.Persons.FirstOrDefaultAsync(x => x.Id == personId, cancellationToken);
        if (person is null)
        {
            throw new NotFoundException($"Person '{personId}' was not found.");
        }

        if (string.IsNullOrWhiteSpace(alias))
        {
            throw new ValidationException("Alias is required.");
        }

        var exists = await _dbContext.Aliases.AnyAsync(x => x.PersonId == personId && x.Value == alias, cancellationToken);
        if (exists)
        {
            throw new ConflictException("Alias already exists for this person.");
        }

        var entity = new Alias
        {
            PersonId = personId,
            Value = alias.Trim()
        };

        _dbContext.Aliases.Add(entity);
        await _dbContext.SaveChangesAsync(cancellationToken);

        return entity;
    }
}
