param(
    [Parameter(Mandatory = $true)]
    [string]$RootDir
)

$ErrorActionPreference = "Stop"

$path = Join-Path $RootDir "apps\api\VeritasAtlas.Infrastructure\Services\WorkflowTransitionService.cs"

if (-not (Test-Path $path)) {
    throw "File not found: $path"
}

$content = Get-Content $path -Raw

$oldBlock = @'
        var claimA = new Claim
        {
            CaseId = @case.Id,
            Topic = "Seed Claim A",
            NormalizedText = "Seed claim A normalized text",
            Status = claimStatus
        };
        Touch(claimA);

        var claimB = new Claim
        {
            CaseId = @case.Id,
            Topic = "Seed Claim B",
            NormalizedText = "Seed claim B normalized text",
            Status = claimStatus
        };
        Touch(claimB);
'@

$newBlock = @'
        var statementId = await _dbContext.Statements
            .OrderByDescending(x => x.CreatedAtUtc)
            .Select(x => x.Id)
            .FirstOrDefaultAsync(cancellationToken);

        if (statementId == Guid.Empty)
        {
            throw new InvalidOperationException("SeedLifecycleAsync requires at least one existing statement. Create one first through /api/v1/statements.");
        }

        var claimA = new Claim
        {
            StatementId = statementId,
            CaseId = @case.Id,
            Topic = "Seed Claim A",
            NormalizedText = "Seed claim A normalized text",
            Status = claimStatus,
            Type = ClaimType.Factual,
            IsMaterial = true
        };
        Touch(claimA);

        var claimB = new Claim
        {
            StatementId = statementId,
            CaseId = @case.Id,
            Topic = "Seed Claim B",
            NormalizedText = "Seed claim B normalized text",
            Status = claimStatus,
            Type = ClaimType.Factual,
            IsMaterial = true
        };
        Touch(claimB);
'@

if ($content.Contains($oldBlock)) {
    $content = $content.Replace($oldBlock, $newBlock)
}
else {
    throw "Expected claim seed block was not found. Open the file and patch it manually."
}

$enc = New-Object System.Text.UTF8Encoding($false)
[System.IO.File]::WriteAllText($path, $content, $enc)

Write-Host "Patched: $path"

Push-Location $RootDir
try {
    dotnet build
    if ($LASTEXITCODE -ne 0) {
        throw "Build failed."
    }
}
finally {
    Pop-Location
}

Write-Host "Build succeeded."