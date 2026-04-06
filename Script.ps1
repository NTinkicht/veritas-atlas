param(
    [string]$RootDir = "C:\Projects\veritas-atlas"
)

$ErrorActionPreference = "Stop"

$apiRoot = Join-Path $RootDir "apps\api"
$applicationRoot = Join-Path $apiRoot "VeritasAtlas.Application"
$infrastructureRoot = Join-Path $apiRoot "VeritasAtlas.Infrastructure"
$apiProjectRoot = Join-Path $apiRoot "VeritasAtlas.Api"

Push-Location $RootDir

Write-Host ""
Write-Host "Checkpointing before Phase 6.7..." -ForegroundColor Cyan

git add -A
git commit -m "checkpoint before phase 6.7 - $(Get-Date -Format 'yyyy-MM-dd HH:mm:ss')" 2>$null

# =========================
# 1. CONTRACTS
# =========================

$contractsPath = Join-Path $applicationRoot "Contracts\Statements\StatementsContracts.cs"

New-Item -ItemType Directory -Force -Path (Split-Path $contractsPath) | Out-Null

@"
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
"@ | Set-Content -Encoding UTF8 $contractsPath

# =========================
# 2. SERVICE UPDATE
# =========================

$servicePath = Join-Path $infrastructureRoot "Services\StatementService.cs"

$content = Get-Content $servicePath -Raw

if ($content -notmatch "GetStatementsAsync") {

$append = @"

using Microsoft.EntityFrameworkCore;
using VeritasAtlas.Application.Contracts.Statements;

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
"@

    $content = $content + $append
    Set-Content -Path $servicePath -Value $content -Encoding UTF8
}

# =========================
# 3. CONTROLLER
# =========================

$controllerPath = Join-Path $apiProjectRoot "Controllers\StatementsController.cs"

@"
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
"@ | Set-Content -Encoding UTF8 $controllerPath

# =========================
# 4. BUILD CHECK
# =========================

$solutionPath = Join-Path $RootDir "VeritasAtlas.slnx"

Write-Host ""
Write-Host "Building..." -ForegroundColor Cyan

dotnet build $solutionPath

Write-Host ""
Write-Host "Phase 6.7 completed successfully." -ForegroundColor Green

Pop-Location