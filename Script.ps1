param(
    [string]$RootDir = "C:\Projects\veritas-atlas"
)

$ErrorActionPreference = "Stop"

$apiRoot = Join-Path $RootDir "apps\api"
$infraRoot = Join-Path $apiRoot "VeritasAtlas.Infrastructure"

$servicePath = Join-Path $infraRoot "Services\StatementService.cs"

Push-Location $RootDir

Write-Host ""
Write-Host "Checkpointing before Phase 6.7 FIX..." -ForegroundColor Cyan

git add -A
git commit -m "fix phase 6.7 - statement service injection error $(Get-Date -Format 'yyyy-MM-dd HH:mm:ss')" 2>$null

# =========================
# CLEAN BAD BLOCK
# =========================

Write-Host "Cleaning invalid appended code..." -ForegroundColor Yellow

$content = Get-Content $servicePath -Raw

# Remove wrongly appended using + methods block
$pattern = "(using Microsoft\.EntityFrameworkCore;[\s\S]*?GetStatementByIdAsync\(Guid id\)[\s\S]*?\})"

$content = [regex]::Replace($content, $pattern, "", "Singleline")

# =========================
# INSERT METHODS PROPERLY
# =========================

Write-Host "Injecting correct methods inside class..." -ForegroundColor Cyan

$methods = @"

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

# Inject BEFORE last closing brace of class
$content = $content -replace "\}\s*$", "$methods`n}"

Set-Content -Path $servicePath -Value $content -Encoding UTF8

# =========================
# BUILD CHECK
# =========================

$solutionPath = Join-Path $RootDir "VeritasAtlas.slnx"

Write-Host ""
Write-Host "Building after fix..." -ForegroundColor Cyan

dotnet build $solutionPath

Pop-Location

Write-Host ""
Write-Host "Phase 6.7 FIX completed successfully." -ForegroundColor Green