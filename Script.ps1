param(
    [string]$RootDir = "C:\Projects\veritas-atlas",
    [string]$ApiProject = "apps\api\VeritasAtlas.Api\VeritasAtlas.Api.csproj",
    [string]$BaseUrl = "http://localhost:5091"
)

$ErrorActionPreference = "Stop"

function Ensure-Directory {
    param([string]$Path)
    if (-not (Test-Path $Path)) {
        New-Item -ItemType Directory -Force -Path $Path | Out-Null
    }
}

function Write-Utf8File {
    param(
        [string]$Path,
        [string]$Content
    )
    $parent = Split-Path -Parent $Path
    Ensure-Directory $parent
    Set-Content -Path $Path -Value $Content -Encoding UTF8
    Write-Host "Wrote: $Path" -ForegroundColor Green
}

function Add-Section {
    param(
        [string]$OutputPath,
        [string]$Title,
        [string]$Content
    )
    Add-Content -Path $OutputPath -Value ""
    Add-Content -Path $OutputPath -Value ("=" * 120)
    Add-Content -Path $OutputPath -Value $Title
    Add-Content -Path $OutputPath -Value ("=" * 120)
    Add-Content -Path $OutputPath -Value $Content
}

function Wait-ForApi {
    param(
        [string]$HealthUrl,
        [int]$MaxAttempts = 40
    )

    for ($i = 1; $i -le $MaxAttempts; $i++) {
        try {
            $null = Invoke-RestMethod -Method Get -Uri $HealthUrl -TimeoutSec 2
            return $true
        }
        catch {
            Start-Sleep -Milliseconds 750
        }
    }

    return $false
}

$solutionPath = Join-Path $RootDir "VeritasAtlas.slnx"
$apiProjectPath = Join-Path $RootDir $ApiProject
$controllerPath = Join-Path $RootDir "apps\api\VeritasAtlas.Api\Controllers\StatementsController.cs"

if (-not (Test-Path $solutionPath)) {
    throw "Solution file not found: $solutionPath"
}

if (-not (Test-Path $apiProjectPath)) {
    throw "API project not found: $apiProjectPath"
}

Push-Location $RootDir

Write-Host ""
Write-Host "Checkpointing current code with git..." -ForegroundColor Cyan

git add -A
if ($LASTEXITCODE -ne 0) {
    Pop-Location
    throw "git add failed."
}

$commitMessage = "checkpoint before statements controller restore - $(Get-Date -Format 'yyyy-MM-dd HH:mm:ss')"
git commit -m $commitMessage 2>$null
if ($LASTEXITCODE -ne 0) {
    Write-Host "No new commit created. Continuing with restore." -ForegroundColor Yellow
}
else {
    Write-Host "Created git commit: $commitMessage" -ForegroundColor Green
}

$controllerContent = @'
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
'@

Write-Host ""
Write-Host "Restoring StatementsController..." -ForegroundColor Cyan
Write-Utf8File -Path $controllerPath -Content $controllerContent

$timestamp = Get-Date -Format "yyyyMMdd-HHmmss"
$diagDir = Join-Path $RootDir "_diagnostics\statements-controller-restore-$timestamp"
Ensure-Directory $diagDir

$reportPath = Join-Path $diagDir "statements-controller-restore-report.txt"
$stdoutPath = Join-Path $diagDir "api-stdout.log"
$stderrPath = Join-Path $diagDir "api-stderr.log"

Set-Content -Path $reportPath -Value "Statements controller restore smoke test`r`nGenerated: $(Get-Date -Format s)`r`nRoot: $RootDir" -Encoding UTF8
Add-Section -OutputPath $reportPath -Title "StatementsController.cs" -Content $controllerContent

Write-Host ""
Write-Host "Building..." -ForegroundColor Cyan

dotnet build $solutionPath
if ($LASTEXITCODE -ne 0) {
    Pop-Location
    throw "dotnet build failed."
}

Write-Host ""
Write-Host "Starting API..." -ForegroundColor Cyan

$apiProcess = Start-Process `
    -FilePath "dotnet" `
    -ArgumentList @("run", "--project", $apiProjectPath, "--no-build") `
    -WorkingDirectory $RootDir `
    -RedirectStandardOutput $stdoutPath `
    -RedirectStandardError $stderrPath `
    -PassThru

try {
    $healthOk = Wait-ForApi -HealthUrl "$BaseUrl/health"
    if (-not $healthOk) {
        throw "API did not become ready."
    }

    $sourceBody = @{
        name = "Statement Restore Source"
        type = "Article"
        reference = "https://example.com/statement-restore"
        createdBy = "statement-restore"
    } | ConvertTo-Json -Depth 5

    $sourceResponse = Invoke-RestMethod -Method Post -Uri "$BaseUrl/api/v1/sources" -ContentType "application/json" -Body $sourceBody
    Add-Section -OutputPath $reportPath -Title "POST /api/v1/sources" -Content (($sourceResponse | ConvertTo-Json -Depth 10))

    $documentBody = @{
        sourceId = $sourceResponse.id
        title = "Statement Restore Document"
        content = "This document is used for the statement controller restore smoke test."
        externalReference = "statement-restore-doc-001"
        createdBy = "statement-restore"
    } | ConvertTo-Json -Depth 5

    $documentResponse = Invoke-RestMethod -Method Post -Uri "$BaseUrl/api/v1/documents" -ContentType "application/json" -Body $documentBody
    Add-Section -OutputPath $reportPath -Title "POST /api/v1/documents" -Content (($documentResponse | ConvertTo-Json -Depth 10))

    $evidenceBody = @{
        documentId = $documentResponse.id
        quote = "This is a quoted evidence snippet for the statement restore smoke test."
        startOffset = 0
        endOffset = 69
        context = "Additional evidence context."
        createdBy = "statement-restore"
    } | ConvertTo-Json -Depth 5

    $evidenceResponse = Invoke-RestMethod -Method Post -Uri "$BaseUrl/api/v1/evidence" -ContentType "application/json" -Body $evidenceBody
    Add-Section -OutputPath $reportPath -Title "POST /api/v1/evidence" -Content (($evidenceResponse | ConvertTo-Json -Depth 10))

    $statementBody = @{
        evidenceId = $evidenceResponse.id
        text = "The subject made a statement during the restored endpoint test."
        createdBy = "statement-restore"
    } | ConvertTo-Json -Depth 5

    $statementCreate = Invoke-RestMethod -Method Post -Uri "$BaseUrl/api/v1/statements" -ContentType "application/json" -Body $statementBody
    Add-Section -OutputPath $reportPath -Title "POST /api/v1/statements" -Content (($statementCreate | ConvertTo-Json -Depth 10))

    $statementList = Invoke-RestMethod -Method Get -Uri "$BaseUrl/api/v1/statements?page=1&pageSize=10"
    Add-Section -OutputPath $reportPath -Title "GET /api/v1/statements" -Content (($statementList | ConvertTo-Json -Depth 10))

    $statementDetail = Invoke-RestMethod -Method Get -Uri "$BaseUrl/api/v1/statements/$($statementCreate.id)"
    Add-Section -OutputPath $reportPath -Title "GET /api/v1/statements/{id}" -Content (($statementDetail | ConvertTo-Json -Depth 10))

    Write-Host ""
    Write-Host "Statements controller restore completed successfully." -ForegroundColor Green
    Write-Host "Report: $reportPath" -ForegroundColor Cyan
}
finally {
    if ($apiProcess -and -not $apiProcess.HasExited) {
        Write-Host ""
        Write-Host "Stopping API..." -ForegroundColor Cyan
        Stop-Process -Id $apiProcess.Id -Force
    }

    Pop-Location
}