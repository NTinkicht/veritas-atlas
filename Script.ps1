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

function Read-FileOrMissing {
    param([string]$Path)
    if (Test-Path $Path) {
        return Get-Content -Path $Path -Raw
    }
    return "[missing]"
}

$solutionPath = Join-Path $RootDir "VeritasAtlas.slnx"
$apiProjectPath = Join-Path $RootDir $ApiProject
$diPath = Join-Path $RootDir "apps\api\VeritasAtlas.Infrastructure\Extensions\ServiceCollectionExtensions.cs"

if (-not (Test-Path $solutionPath)) {
    throw "Solution file not found: $solutionPath"
}

Push-Location $RootDir

Write-Host ""
Write-Host "Checkpointing current code with git..." -ForegroundColor Cyan

git add -A
if ($LASTEXITCODE -ne 0) {
    Pop-Location
    throw "git add failed."
}

$commitMessage = "checkpoint before statement di fix - $(Get-Date -Format 'yyyy-MM-dd HH:mm:ss')"
git commit -m $commitMessage 2>$null
if ($LASTEXITCODE -ne 0) {
    Write-Host "No new commit created. Continuing with DI fix." -ForegroundColor Yellow
}
else {
    Write-Host "Created git commit: $commitMessage" -ForegroundColor Green
}

$timestamp = Get-Date -Format "yyyyMMdd-HHmmss"
$diagDir = Join-Path $RootDir "_diagnostics\statement-di-fix-$timestamp"
Ensure-Directory $diagDir

$reportPath = Join-Path $diagDir "statement-di-fix-report.txt"
$stdoutPath = Join-Path $diagDir "api-stdout.log"
$stderrPath = Join-Path $diagDir "api-stderr.log"

Set-Content -Path $reportPath -Value "Statement DI fix smoke test`r`nGenerated: $(Get-Date -Format s)`r`nRoot: $RootDir" -Encoding UTF8

$beforeDi = Get-Content $diPath -Raw
Add-Section -OutputPath $reportPath -Title "ServiceCollectionExtensions.cs BEFORE" -Content $beforeDi

if ($beforeDi -notmatch 'AddScoped<StatementService>\(\);') {
    $afterDi = $beforeDi -replace 'services\.AddScoped<IStatementService,\s*StatementService>\(\);', "services.AddScoped<IStatementService, StatementService>();`r`n        services.AddScoped<StatementService>();"
    Write-Utf8File -Path $diPath -Content $afterDi
}
else {
    $afterDi = $beforeDi
}

Add-Section -OutputPath $reportPath -Title "ServiceCollectionExtensions.cs AFTER" -Content (Get-Content $diPath -Raw)

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
        name = "Statement DI Fix Source"
        type = "Article"
        reference = "https://example.com/statement-di-fix"
        createdBy = "statement-di-fix"
    } | ConvertTo-Json -Depth 5

    $sourceResponse = Invoke-RestMethod -Method Post -Uri "$BaseUrl/api/v1/sources" -ContentType "application/json" -Body $sourceBody
    Add-Section -OutputPath $reportPath -Title "POST /api/v1/sources" -Content (($sourceResponse | ConvertTo-Json -Depth 10))

    $documentBody = @{
        sourceId = $sourceResponse.id
        title = "Statement DI Fix Document"
        content = "This document is used for the statement DI fix smoke test."
        externalReference = "statement-di-fix-doc-001"
        createdBy = "statement-di-fix"
    } | ConvertTo-Json -Depth 5

    $documentResponse = Invoke-RestMethod -Method Post -Uri "$BaseUrl/api/v1/documents" -ContentType "application/json" -Body $documentBody
    Add-Section -OutputPath $reportPath -Title "POST /api/v1/documents" -Content (($documentResponse | ConvertTo-Json -Depth 10))

    $evidenceBody = @{
        documentId = $documentResponse.id
        quote = "This is a quoted evidence snippet for the statement DI fix smoke test."
        startOffset = 0
        endOffset = 68
        context = "Additional evidence context."
        createdBy = "statement-di-fix"
    } | ConvertTo-Json -Depth 5

    $evidenceResponse = Invoke-RestMethod -Method Post -Uri "$BaseUrl/api/v1/evidence" -ContentType "application/json" -Body $evidenceBody
    Add-Section -OutputPath $reportPath -Title "POST /api/v1/evidence" -Content (($evidenceResponse | ConvertTo-Json -Depth 10))

    try {
        $statementBody = @{
            evidenceId = $evidenceResponse.id
            text = "The subject made a statement during the DI fix verification."
            createdBy = "statement-di-fix"
        } | ConvertTo-Json -Depth 5

        $statementCreate = Invoke-RestMethod -Method Post -Uri "$BaseUrl/api/v1/statements" -ContentType "application/json" -Body $statementBody
        Add-Section -OutputPath $reportPath -Title "POST /api/v1/statements" -Content (($statementCreate | ConvertTo-Json -Depth 10))

        $statementList = Invoke-RestMethod -Method Get -Uri "$BaseUrl/api/v1/statements?page=1&pageSize=10"
        Add-Section -OutputPath $reportPath -Title "GET /api/v1/statements" -Content (($statementList | ConvertTo-Json -Depth 10))

        $statementDetail = Invoke-RestMethod -Method Get -Uri "$BaseUrl/api/v1/statements/$($statementCreate.id)"
        Add-Section -OutputPath $reportPath -Title "GET /api/v1/statements/{id}" -Content (($statementDetail | ConvertTo-Json -Depth 10))
    }
    catch {
        Add-Section -OutputPath $reportPath -Title "STATEMENT REQUEST FAILURE" -Content $_.Exception.ToString()
        Add-Section -OutputPath $reportPath -Title "API STDOUT" -Content (Read-FileOrMissing $stdoutPath)
        Add-Section -OutputPath $reportPath -Title "API STDERR" -Content (Read-FileOrMissing $stderrPath)
        throw
    }

    Write-Host ""
    Write-Host "Statement DI fix completed successfully." -ForegroundColor Green
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