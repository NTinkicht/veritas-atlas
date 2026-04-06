using VeritasAtlas.Application.Models;
using VeritasAtlas.Domain.Entities;

namespace VeritasAtlas.Application.Interfaces;

public interface IPersonService
{
    Task<PagedResult<Person>> GetPersonsAsync(PersonListFilters filters, CancellationToken cancellationToken = default);
    Task<Person> GetPersonByIdAsync(Guid id, CancellationToken cancellationToken = default);
    Task<Person> CreatePersonAsync(string displayName, string? createdBy = null, CancellationToken cancellationToken = default);
    Task<Alias> AddAliasAsync(Guid personId, string alias, string? createdBy = null, CancellationToken cancellationToken = default);
}
