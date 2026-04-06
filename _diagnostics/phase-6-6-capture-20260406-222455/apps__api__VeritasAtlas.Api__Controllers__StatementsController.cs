using Microsoft.AspNetCore.Mvc;
using VeritasAtlas.Api.Contracts.Statements;
using VeritasAtlas.Application.Interfaces;

namespace VeritasAtlas.Api.Controllers;

[ApiController]
[Route("api/v1/statements")]
public sealed class StatementsController : ControllerBase
{
    private readonly IStatementService _statementService;

    public StatementsController(IStatementService statementService)
    {
        _statementService = statementService;
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
}
