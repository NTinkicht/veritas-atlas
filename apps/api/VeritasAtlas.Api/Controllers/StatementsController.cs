using Microsoft.AspNetCore.Mvc;
using VeritasAtlas.Application.Contracts.Statements;
using VeritasAtlas.Infrastructure.Services;

namespace VeritasAtlas.Api.Controllers;

[ApiController]
[Route("api/statements")]
public class StatementsController : ControllerBase
{
    private readonly StatementService _service;

    public StatementsController(StatementService service)
    {
        _service = service;
    }

    [HttpGet]
    public async Task<IActionResult> GetStatements([FromQuery] StatementListRequest request)
    {
        var (total, items) = await _service.GetStatementsAsync(request.Page, request.PageSize);

        return Ok(new
        {
            Total = total,
            Page = request.Page,
            PageSize = request.PageSize,
            Items = items
        });
    }

    [HttpGet("{id:guid}")]
    public async Task<IActionResult> GetStatement(Guid id)
    {
        var result = await _service.GetStatementByIdAsync(id);

        if (result == null)
            return NotFound();

        return Ok(result);
    }
}
