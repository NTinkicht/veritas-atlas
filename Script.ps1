param(
    [Parameter(Mandatory = $true)]
    [string]$RootDir
)

$ErrorActionPreference = "Stop"

function Ensure-Dir {
    param([string]$Path)
    if ([string]::IsNullOrWhiteSpace($Path)) {
        throw "Ensure-Dir received an empty path."
    }
    if (-not (Test-Path $Path)) {
        New-Item -ItemType Directory -Path $Path -Force | Out-Null
    }
}

function Write-File {
    param(
        [string]$Path,
        [string]$Content
    )
    if ([string]::IsNullOrWhiteSpace($Path)) {
        throw "Write-File received an empty path."
    }
    $parent = Split-Path -Parent $Path
    Ensure-Dir $parent
    $enc = New-Object System.Text.UTF8Encoding($false)
    [System.IO.File]::WriteAllText($Path, $Content, $enc)
    Write-Host "Wrote: $Path"
}

function Git-Checkpoint {
    param([string]$Message)

    Push-Location $RootDir
    try {
        if (Test-Path ".git") {
            git add -A | Out-Null
            git commit -m $Message 2>$null | Out-Null
            if ($LASTEXITCODE -eq 0) {
                Write-Host "Created git commit: $Message"
            }
            else {
                Write-Host "No new commit created. Continuing."
            }
        }
    }
    finally {
        Pop-Location
    }
}

function Build-All {
    param([string]$RootDir)

    Push-Location $RootDir
    try {
        dotnet build
        if ($LASTEXITCODE -ne 0) { throw "Backend failed" }
    }
    finally {
        Pop-Location
    }

    $web = Join-Path $RootDir "apps\web\veritas-atlas-web"
    Push-Location $web
    try {
        npm run build
        if ($LASTEXITCODE -ne 0) { throw "Frontend failed" }
    }
    finally {
        Pop-Location
    }
}

Write-Host "Checkpointing current code with git..." -ForegroundColor Cyan
Git-Checkpoint -Message ("checkpoint before phase 7.1 - " + (Get-Date -Format "yyyy-MM-dd HH:mm:ss"))

Write-Host "Applying Phase 7.1 - backend depth starter: smoke tests, diagnostics, and delivery report..." -ForegroundColor Cyan

$root = $RootDir
$diag = Join-Path $root "_diagnostics\phase-7-1"
Ensure-Dir $diag

$scriptPath = Join-Path $root "tools\smoke\Run-VeritasAtlas-Smoke.ps1"
$reportPath = Join-Path $diag "phase-7-1-readiness-report.md"

Write-File $scriptPath @'
param(
    [Parameter(Mandatory = $true)]
    [string]$RootDir,
    [string]$BaseUrl = "http://localhost:5209"
)

$ErrorActionPreference = "Stop"

function Ensure-Dir {
    param([string]$Path)
    if (-not (Test-Path $Path)) {
        New-Item -ItemType Directory -Path $Path -Force | Out-Null
    }
}

function Append-Line {
    param(
        [string]$Path,
        [string]$Text
    )
    Add-Content -Path $Path -Value $Text
}

$timestamp = Get-Date -Format "yyyyMMdd-HHmmss"
$diag = Join-Path $RootDir "_diagnostics\smoke-$timestamp"
Ensure-Dir $diag
$report = Join-Path $diag "smoke-report.md"
$apiLog = Join-Path $diag "api.log"

Set-Content -Path $report -Value "# Veritas Atlas Smoke Report`r`n" -Encoding UTF8
Append-Line -Path $report -Text ("Generated: " + (Get-Date -Format "yyyy-MM-dd HH:mm:ss"))
Append-Line -Path $report -Text ""

Push-Location (Join-Path $RootDir "apps\api\VeritasAtlas.Api")
$apiProcess = $null
try {
    $apiProcess = Start-Process "dotnet" -ArgumentList "run" -RedirectStandardOutput $apiLog -RedirectStandardError $apiLog -PassThru
    Start-Sleep -Seconds 8

    $checks = @(
        @{ Name = "Health"; Url = "$BaseUrl/health" },
        @{ Name = "Database Health"; Url = "$BaseUrl/health/db" },
        @{ Name = "Cases"; Url = "$BaseUrl/api/v1/cases?page=1&pageSize=5" },
        @{ Name = "Sources"; Url = "$BaseUrl/api/v1/sources?page=1&pageSize=5" },
        @{ Name = "Documents"; Url = "$BaseUrl/api/v1/documents?page=1&pageSize=5" },
        @{ Name = "Evidence"; Url = "$BaseUrl/api/v1/evidence?page=1&pageSize=5" },
        @{ Name = "Statements"; Url = "$BaseUrl/api/v1/statements?page=1&pageSize=5" },
        @{ Name = "Claims"; Url = "$BaseUrl/api/v1/claims?page=1&pageSize=5" },
        @{ Name = "Contradictions"; Url = "$BaseUrl/api/v1/contradictions?page=1&pageSize=5" }
    )

    foreach ($check in $checks) {
        try {
            $response = Invoke-WebRequest -Uri $check.Url -Method Get -UseBasicParsing -TimeoutSec 15
            Append-Line -Path $report -Text ("## " + $check.Name)
            Append-Line -Path $report -Text ("- StatusCode: " + $response.StatusCode)
            Append-Line -Path $report -Text ("- Url: " + $check.Url)
            Append-Line -Path $report -Text ""
        }
        catch {
            Append-Line -Path $report -Text ("## " + $check.Name)
            Append-Line -Path $report -Text ("- FAILED: " + $_.Exception.Message)
            Append-Line -Path $report -Text ("- Url: " + $check.Url)
            Append-Line -Path $report -Text ""
        }
    }

    Append-Line -Path $report -Text "## API log"
    Append-Line -Path $report -Text ""
    if (Test-Path $apiLog) {
        Append-Line -Path $report -Text '```'
        Append-Line -Path $report -Text (Get-Content $apiLog -Raw)
        Append-Line -Path $report -Text '```'
    }
}
finally {
    Pop-Location
    if ($apiProcess -and -not $apiProcess.HasExited) {
        Stop-Process -Id $apiProcess.Id -Force
    }
}
'@

Write-File $reportPath @'
# Phase 7.1 Readiness Report

This phase moves beyond broad UI expansion and starts the deeper implementation track.

## Added
- Smoke test runner script
- Delivery diagnostics folder
- Readiness report scaffold

## Purpose
- verify API startup
- verify key list endpoints
- capture logs
- establish a repeatable backend validation loop

## Next recommended track
- real review write actions
- real contradiction resolution actions
- publication state transitions
- confidence scoring backend
- end-to-end seeded data scenario
'@

Write-Host "Building..." -ForegroundColor Cyan
Build-All -RootDir $RootDir

Write-Host "Phase 7.1 DONE" -ForegroundColor Green
