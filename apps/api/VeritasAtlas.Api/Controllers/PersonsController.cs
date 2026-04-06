using Microsoft.AspNetCore.Mvc;
using VeritasAtlas.Api.Contracts.Persons;
using VeritasAtlas.Application.Interfaces;
using VeritasAtlas.Application.Models;

namespace VeritasAtlas.Api.Controllers;

[ApiController]
[Route("api/v1/persons")]
public sealed class PersonsController : ControllerBase
{
    private readonly IPersonService _personService;

    public PersonsController(IPersonService personService)
    {
        _personService = personService;
    }

    [HttpGet]
    public async Task<ActionResult<GetPersonsResponse>> GetPersons(
        [FromQuery] int page = 1,
        [FromQuery] int pageSize = 20,
        [FromQuery] string? search = null,
        CancellationToken cancellationToken = default)
    {
        var result = await _personService.GetPersonsAsync(
            new PersonListFilters
            {
                Page = page,
                PageSize = pageSize,
                Search = search
            },
            cancellationToken);

        return Ok(new GetPersonsResponse(
            result.Items.Select(MapPerson).ToArray(),
            result.Page,
            result.PageSize,
            result.TotalCount,
            result.TotalPages));
    }

    [HttpGet("{id:guid}")]
    public async Task<ActionResult<GetPersonResponse>> GetPersonById(Guid id, CancellationToken cancellationToken = default)
    {
        var person = await _personService.GetPersonByIdAsync(id, cancellationToken);
        return Ok(MapPerson(person));
    }

    [HttpPost]
    public async Task<ActionResult<CreatePersonResponse>> CreatePerson(
        [FromBody] CreatePersonRequest request,
        CancellationToken cancellationToken = default)
    {
        var person = await _personService.CreatePersonAsync(request.DisplayName, request.CreatedBy, cancellationToken);

        var response = new CreatePersonResponse(
            person.Id,
            person.Name.DisplayName,
            person.CreatedAtUtc);

        return CreatedAtAction(nameof(GetPersonById), new { id = person.Id }, response);
    }

    [HttpPost("{id:guid}/aliases")]
    public async Task<ActionResult<AddAliasResponse>> AddAlias(
        Guid id,
        [FromBody] AddAliasRequest request,
        CancellationToken cancellationToken = default)
    {
        var alias = await _personService.AddAliasAsync(id, request.Alias, request.CreatedBy, cancellationToken);

        return Ok(new AddAliasResponse(
            alias.Id,
            alias.PersonId,
            alias.Value,
            alias.CreatedAtUtc));
    }

    private static GetPersonResponse MapPerson(VeritasAtlas.Domain.Entities.Person person)
    {
        return new GetPersonResponse(
            person.Id,
            person.Name.DisplayName,
            person.Description,
            person.Nationality,
            person.CreatedAtUtc,
            person.UpdatedAtUtc,
            person.Aliases.Select(x => x.Value).ToArray());
    }
}
