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

$commitMessage = "checkpoint before statement smoke test - $(Get-Date -Format 'yyyy-MM-dd HH:mm:ss')"
git commit -m $commitMessage 2>$null
if ($LASTEXITCODE -ne 0) {
    Write-Host "No new commit created. Continuing with smoke test." -ForegroundColor Yellow
}
else {
    Write-Host "Created git commit: $commitMessage" -ForegroundColor Green
}

$timestamp = Get-Date -Format "yyyyMMdd-HHmmss"
$diagDir = Join-Path $RootDir "_diagnostics\statement-smoke-$timestamp"
Ensure-Directory $diagDir

$reportPath = Join-Path $diagDir "statement-smoke-report.txt"
$stdoutPath = Join-Path $diagDir "api-stdout.log"
$stderrPath = Join-Path $diagDir "api-stderr.log"

Set-Content -Path $reportPath -Value "Statement smoke test`r`nGenerated: $(Get-Date -Format s)`r`nRoot: $RootDir" -Encoding UTF8

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

    Write-Host "API is reachable." -ForegroundColor Green

    $sourceBody = @{
        name = "Statement Smoke Source"
        type = "Article"
        reference = "https://example.com/statement-smoke"
        createdBy = "statement-smoke"
    } | ConvertTo-Json -Depth 5

    $sourceResponse = Invoke-RestMethod `
        -Method Post `
        -Uri "$BaseUrl/api/v1/sources" `
        -ContentType "application/json" `
        -Body $sourceBody

    Add-Section -OutputPath $reportPath -Title "POST /api/v1/sources" -Content (($sourceResponse | ConvertTo-Json -Depth 10))

    $documentBody = @{
        sourceId = $sourceResponse.id
        title = "Statement Smoke Document"
        content = "This is a document used for statement smoke testing."
        externalReference = "statement-smoke-doc-001"
        createdBy = "statement-smoke"
    } | ConvertTo-Json -Depth 5

    $documentResponse = Invoke-RestMethod `
        -Method Post `
        -Uri "$BaseUrl/api/v1/documents" `
        -ContentType "application/json" `
        -Body $documentBody

    Add-Section -OutputPath $reportPath -Title "POST /api/v1/documents" -Content (($documentResponse | ConvertTo-Json -Depth 10))

    $evidenceBody = @{
        documentId = $documentResponse.id
        quote = "This is a quoted evidence snippet for statement testing."
        startOffset = 0
        endOffset = 54
        context = "Additional evidence context."
        createdBy = "statement-smoke"
    } | ConvertTo-Json -Depth 5

    $evidenceResponse = Invoke-RestMethod `
        -Method Post `
        -Uri "$BaseUrl/api/v1/evidence" `
        -ContentType "application/json" `
        -Body $evidenceBody

    Add-Section -OutputPath $reportPath -Title "POST /api/v1/evidence" -Content (($evidenceResponse | ConvertTo-Json -Depth 10))

    $statementBody = @{
        evidenceId = $evidenceResponse.id
        text = "The subject made a statement during the recorded event."
        createdBy = "statement-smoke"
    } | ConvertTo-Json -Depth 5

    $statementCreateResponse = Invoke-RestMethod `
        -Method Post `
        -Uri "$BaseUrl/api/v1/statements" `
        -ContentType "application/json" `
        -Body $statementBody

    Add-Section -OutputPath $reportPath -Title "POST /api/v1/statements" -Content (($statementCreateResponse | ConvertTo-Json -Depth 10))

    $statementListV1Succeeded = $false
    $statementListApiSucceeded = $false
    $statementDetailV1Succeeded = $false
    $statementDetailApiSucceeded = $false

    try {
        $statementListV1 = Invoke-RestMethod -Method Get -Uri "$BaseUrl/api/v1/statements?page=1&pageSize=10"
        $statementListV1Succeeded = $true
        Add-Section -OutputPath $reportPath -Title "GET /api/v1/statements" -Content (($statementListV1 | ConvertTo-Json -Depth 10))
    }
    catch {
        Add-Section -OutputPath $reportPath -Title "GET /api/v1/statements" -Content $_.Exception.ToString()
    }

    try {
        $statementListApi = Invoke-RestMethod -Method Get -Uri "$BaseUrl/api/statements?page=1&pageSize=10"
        $statementListApiSucceeded = $true
        Add-Section -OutputPath $reportPath -Title "GET /api/statements" -Content (($statementListApi | ConvertTo-Json -Depth 10))
    }
    catch {
        Add-Section -OutputPath $reportPath -Title "GET /api/statements" -Content $_.Exception.ToString()
    }

    try {
        $statementDetailV1 = Invoke-RestMethod -Method Get -Uri "$BaseUrl/api/v1/statements/$($statementCreateResponse.id)"
        $statementDetailV1Succeeded = $true
        Add-Section -OutputPath $reportPath -Title "GET /api/v1/statements/{id}" -Content (($statementDetailV1 | ConvertTo-Json -Depth 10))
    }
    catch {
        Add-Section -OutputPath $reportPath -Title "GET /api/v1/statements/{id}" -Content $_.Exception.ToString()
    }

    try {
        $statementDetailApi = Invoke-RestMethod -Method Get -Uri "$BaseUrl/api/statements/$($statementCreateResponse.id)"
        $statementDetailApiSucceeded = $true
        Add-Section -OutputPath $reportPath -Title "GET /api/statements/{id}" -Content (($statementDetailApi | ConvertTo-Json -Depth 10))
    }
    catch {
        Add-Section -OutputPath $reportPath -Title "GET /api/statements/{id}" -Content $_.Exception.ToString()
    }

    $summary = @(
        "POST /api/v1/statements succeeded: True",
        "GET /api/v1/statements succeeded: $statementListV1Succeeded",
        "GET /api/statements succeeded: $statementListApiSucceeded",
        "GET /api/v1/statements/{id} succeeded: $statementDetailV1Succeeded",
        "GET /api/statements/{id} succeeded: $statementDetailApiSucceeded"
    ) -join [Environment]::NewLine

    Add-Section -OutputPath $reportPath -Title "SUMMARY" -Content $summary

    Write-Host ""
    Write-Host "Statement smoke test completed." -ForegroundColor Green
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