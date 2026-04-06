using Microsoft.AspNetCore.Mvc;
using VeritasAtlas.Api.Contracts.Statements;
using VeritasAtlas.Application.Contracts.Statements;
using VeritasAtlas.Application.Interfaces;
using VeritasAtlas.Infrastructure.Services;

namespace VeritasAtlas.Api.Controllers;

[ApiController]
[Route("api/v1/statements")]
public class StatementsController : ControllerBase
{
    private readonly IStatementService _statementService;
    private readonly StatementService _statementQueryService;

    public StatementsController(
        IStatementService statementService,
        StatementService statementQueryService)
    {
        _statementService = statementService;
        _statementQueryService = statementQueryService;
    }

    [HttpPost]
    public async Task<ActionResult<CreateStatementResponse>> CreateStatement(
        [FromBody] CreateStatementRequest request,
        CancellationToken cancellationToken = default)
    {
        var entity = await _statementService.ExtractStatementAsync(
            request.EvidenceId,
            request.Text,
            request.CreatedBy,
            cancellationToken);

        var response = new CreateStatementResponse(
            entity.Id,
            entity.EvidenceId,
            entity.DocumentId,
            entity.Text.ToString(),
            entity.Polarity.ToString(),
            entity.Status.ToString(),
            entity.Topic,
            entity.CreatedAtUtc,
            entity.UpdatedAtUtc);

        return Ok(response);
    }

    [HttpGet]
    public async Task<IActionResult> GetStatements([FromQuery] StatementListRequest request)
    {
        var (total, items) = await _statementQueryService.GetStatementsAsync(request.Page, request.PageSize);

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
        var result = await _statementQueryService.GetStatementByIdAsync(id);

        if (result == null)
        {
            return NotFound();
        }

        return Ok(result);
    }
}
