param(
    [string]$RootDir = "C:\Projects\veritas-atlas"
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

Push-Location $RootDir

Write-Host ""
Write-Host "Checkpointing before StatementService hard repair..." -ForegroundColor Cyan

git add -A
if ($LASTEXITCODE -ne 0) {
    Pop-Location
    throw "git add failed."
}

$commitMessage = "checkpoint before statementservice hard repair - $(Get-Date -Format 'yyyy-MM-dd HH:mm:ss')"
git commit -m $commitMessage 2>$null
if ($LASTEXITCODE -ne 0) {
    Write-Host "No new commit created. Continuing with repair." -ForegroundColor Yellow
}
else {
    Write-Host "Created git commit: $commitMessage" -ForegroundColor Green
}

Pop-Location

$servicePath = Join-Path $RootDir "apps\api\VeritasAtlas.Infrastructure\Services\StatementService.cs"
$solutionPath = Join-Path $RootDir "VeritasAtlas.slnx"

$serviceContent = @'
using Microsoft.EntityFrameworkCore;
using VeritasAtlas.Application.Common;
using VeritasAtlas.Application.Contracts.Statements;
using VeritasAtlas.Application.Interfaces;
using VeritasAtlas.Domain.Entities;
using VeritasAtlas.Domain.Enums;
using VeritasAtlas.Domain.ValueObjects;
using VeritasAtlas.Infrastructure.Persistence;

namespace VeritasAtlas.Infrastructure.Services;

public sealed class StatementService : IStatementService
{
    private readonly VeritasAtlasDbContext _dbContext;

    public StatementService(VeritasAtlasDbContext dbContext)
    {
        _dbContext = dbContext;
    }

    public async Task<Statement> ExtractStatementAsync(
        Guid evidenceId,
        string text,
        string? createdBy = null,
        CancellationToken cancellationToken = default)
    {
        var evidence = await _dbContext.Evidences
            .FirstOrDefaultAsync(x => x.Id == evidenceId, cancellationToken);

        if (evidence is null)
        {
            throw new NotFoundException($"Evidence '{evidenceId}' was not found.");
        }

        if (string.IsNullOrWhiteSpace(text))
        {
            throw new ValidationException("Statement text is required.");
        }

        var normalized = text.Trim();

        var entity = new Statement
        {
            EvidenceId = evidenceId,
            DocumentId = evidence.DocumentId,
            Text = new StatementText(normalized, normalized),
            Polarity = StatementPolarity.Affirmative,
            Status = StatementStatus.Extracted,
            Topic = "general"
        };

        _dbContext.Statements.Add(entity);
        await _dbContext.SaveChangesAsync(cancellationToken);

        return entity;
    }

    public async Task<(int Total, List<StatementListItemResponse> Items)> GetStatementsAsync(
        int page,
        int pageSize)
    {
        page = page < 1 ? 1 : page;
        pageSize = pageSize < 1 ? 20 : pageSize;

        var query = _dbContext.Statements.AsNoTracking();

        var total = await query.CountAsync();

        var items = await query
            .OrderByDescending(x => x.CreatedAtUtc)
            .Skip((page - 1) * pageSize)
            .Take(pageSize)
            .Select(x => new StatementListItemResponse
            {
                Id = x.Id,
                Text = x.Text.Raw,
                Topic = x.Topic,
                Predicate = x.Predicate,
                Object = x.ObjectValue,
                Polarity = x.Polarity.ToString(),
                Status = x.Status.ToString(),
                CreatedAt = x.CreatedAtUtc
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
                Text = x.Text.Raw,
                Topic = x.Topic,
                Predicate = x.Predicate,
                Object = x.ObjectValue,
                Polarity = x.Polarity.ToString(),
                Status = x.Status.ToString(),
                EvidenceId = x.EvidenceId,
                PersonId = x.PersonId,
                CreatedAt = x.CreatedAtUtc
            })
            .FirstOrDefaultAsync();
    }
}
'@

Write-Host ""
Write-Host "Replacing StatementService.cs with a clean version..." -ForegroundColor Cyan
Write-Utf8File -Path $servicePath -Content $serviceContent

Write-Host ""
Write-Host "Building after hard repair..." -ForegroundColor Cyan

Push-Location $RootDir
dotnet build $solutionPath
$buildExit = $LASTEXITCODE
Pop-Location

if ($buildExit -ne 0) {
    throw "dotnet build failed."
}

Write-Host ""
Write-Host "StatementService hard repair completed successfully." -ForegroundColor Green