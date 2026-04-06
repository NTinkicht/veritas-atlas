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

function Try-Request {
    param(
        [string]$Method,
        [string]$Uri,
        [string]$Body = $null
    )

    try {
        if ($null -ne $Body) {
            $response = Invoke-RestMethod -Method $Method -Uri $Uri -ContentType "application/json" -Body $Body
        }
        else {
            $response = Invoke-RestMethod -Method $Method -Uri $Uri
        }

        return @{
            Success = $true
            Data = ($response | ConvertTo-Json -Depth 20)
        }
    }
    catch {
        return @{
            Success = $false
            Data = $_.Exception.ToString()
        }
    }
}

$solutionPath = Join-Path $RootDir "VeritasAtlas.slnx"
$apiProjectPath = Join-Path $RootDir $ApiProject

$controllerPath = Join-Path $RootDir "apps\api\VeritasAtlas.Api\Controllers\StatementsController.cs"
$contractsPath = Join-Path $RootDir "apps\api\VeritasAtlas.Api\Contracts\StatementsContracts.cs"
$servicePath = Join-Path $RootDir "apps\api\VeritasAtlas.Infrastructure\Services\StatementService.cs"
$interfacePath = Join-Path $RootDir "apps\api\VeritasAtlas.Application\Interfaces\IStatementService.cs"

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

$commitMessage = "checkpoint before statement route repair - $(Get-Date -Format 'yyyy-MM-dd HH:mm:ss')"
git commit -m $commitMessage 2>$null
if ($LASTEXITCODE -ne 0) {
    Write-Host "No new commit created. Continuing." -ForegroundColor Yellow
}
else {
    Write-Host "Created git commit: $commitMessage" -ForegroundColor Green
}

$timestamp = Get-Date -Format "yyyyMMdd-HHmmss"
$diagDir = Join-Path $RootDir "_diagnostics\statement-route-repair-$timestamp"
Ensure-Directory $diagDir

$reportPath = Join-Path $diagDir "statement-route-repair-report.txt"
$stdoutPath = Join-Path $diagDir "api-stdout.log"
$stderrPath = Join-Path $diagDir "api-stderr.log"

Set-Content -Path $reportPath -Value "Statement route repair and smoke test`r`nGenerated: $(Get-Date -Format s)`r`nRoot: $RootDir" -Encoding UTF8

Add-Section -OutputPath $reportPath -Title "FILE: StatementsController.cs BEFORE" -Content (Get-Content $controllerPath -Raw)
Add-Section -OutputPath $reportPath -Title "FILE: StatementsContracts.cs" -Content (Get-Content $contractsPath -Raw)
Add-Section -OutputPath $reportPath -Title "FILE: IStatementService.cs" -Content (Get-Content $interfacePath -Raw)
Add-Section -OutputPath $reportPath -Title "FILE: StatementService.cs" -Content (Get-Content $servicePath -Raw)

Write-Host ""
Write-Host "Building..." -ForegroundColor Cyan

dotnet build $solutionPath
if ($LASTEXITCODE -ne 0) {
    Pop-Location
    throw "dotnet build failed."
}

Write-Host ""
Write-Host "Starting API for route probe..." -ForegroundColor Cyan

$apiProcess = Start-Process `
    -FilePath "dotnet" `
    -ArgumentList @("run", "--project", $apiProjectPath, "--no-build") `
    -WorkingDirectory $RootDir `
    -RedirectStandardOutput $stdoutPath `
    -RedirectStandardError $stderrPath `
    -PassThru

$routeNeedsPatch = $false

try {
    $healthOk = Wait-ForApi -HealthUrl "$BaseUrl/health"
    if (-not $healthOk) {
        throw "API did not become ready."
    }

    Write-Host "API is reachable." -ForegroundColor Green

    $probeBody = @{
        evidenceId = "00000000-0000-0000-0000-000000000001"
        text = "probe"
        createdBy = "route-probe"
    } | ConvertTo-Json -Depth 5

    $probeV1 = Try-Request -Method "Post" -Uri "$BaseUrl/api/v1/statements" -Body $probeBody
    Add-Section -OutputPath $reportPath -Title "PROBE POST /api/v1/statements" -Content $probeV1.Data

    $probePlain = Try-Request -Method "Post" -Uri "$BaseUrl/api/statements" -Body $probeBody
    Add-Section -OutputPath $reportPath -Title "PROBE POST /api/statements" -Content $probePlain.Data

    if (-not $probeV1.Success -and $probePlain.Success) {
        $routeNeedsPatch = $true
    }

    if ($apiProcess -and -not $apiProcess.HasExited) {
        Stop-Process -Id $apiProcess.Id -Force
    }
}
finally {
    Start-Sleep -Milliseconds 500
}

if ($routeNeedsPatch) {
    Write-Host ""
    Write-Host "Patching StatementsController route to /api/v1/statements..." -ForegroundColor Cyan

    $controllerContent = Get-Content $controllerPath -Raw
    $controllerContent = $controllerContent -replace '\[Route\("api/statements"\)\]', '[Route("api/v1/statements")]'
    Write-Utf8File -Path $controllerPath -Content $controllerContent

    Add-Section -OutputPath $reportPath -Title "FILE: StatementsController.cs AFTER PATCH" -Content (Get-Content $controllerPath -Raw)

    Write-Host ""
    Write-Host "Rebuilding after route patch..." -ForegroundColor Cyan

    dotnet build $solutionPath
    if ($LASTEXITCODE -ne 0) {
        Pop-Location
        throw "dotnet build failed after route patch."
    }
}
else {
    Add-Section -OutputPath $reportPath -Title "PATCH DECISION" -Content "No route patch was applied."
}

Write-Host ""
Write-Host "Starting API for full smoke test..." -ForegroundColor Cyan

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
        throw "API did not become ready after patch/build."
    }

    $sourceBody = @{
        name = "Statement Route Repair Source"
        type = "Article"
        reference = "https://example.com/statement-route-repair"
        createdBy = "statement-route-repair"
    } | ConvertTo-Json -Depth 5

    $sourceResponse = Invoke-RestMethod -Method Post -Uri "$BaseUrl/api/v1/sources" -ContentType "application/json" -Body $sourceBody
    Add-Section -OutputPath $reportPath -Title "POST /api/v1/sources" -Content (($sourceResponse | ConvertTo-Json -Depth 10))

    $documentBody = @{
        sourceId = $sourceResponse.id
        title = "Statement Route Repair Document"
        content = "This is a document for statement route repair smoke testing."
        externalReference = "statement-route-repair-doc-001"
        createdBy = "statement-route-repair"
    } | ConvertTo-Json -Depth 5

    $documentResponse = Invoke-RestMethod -Method Post -Uri "$BaseUrl/api/v1/documents" -ContentType "application/json" -Body $documentBody
    Add-Section -OutputPath $reportPath -Title "POST /api/v1/documents" -Content (($documentResponse | ConvertTo-Json -Depth 10))

    $evidenceBody = @{
        documentId = $documentResponse.id
        quote = "This is a quoted evidence snippet for statement route repair."
        startOffset = 0
        endOffset = 60
        context = "Additional evidence context."
        createdBy = "statement-route-repair"
    } | ConvertTo-Json -Depth 5

    $evidenceResponse = Invoke-RestMethod -Method Post -Uri "$BaseUrl/api/v1/evidence" -ContentType "application/json" -Body $evidenceBody
    Add-Section -OutputPath $reportPath -Title "POST /api/v1/evidence" -Content (($evidenceResponse | ConvertTo-Json -Depth 10))

    $statementBody = @{
        evidenceId = $evidenceResponse.id
        text = "The subject made a statement during the recorded event."
        createdBy = "statement-route-repair"
    } | ConvertTo-Json -Depth 5

    $statementCreate = Try-Request -Method "Post" -Uri "$BaseUrl/api/v1/statements" -Body $statementBody
    Add-Section -OutputPath $reportPath -Title "POST /api/v1/statements" -Content $statementCreate.Data

    if (-not $statementCreate.Success) {
        throw "Statement create still failed at /api/v1/statements."
    }

    $statementObject = $statementCreate.Data | ConvertFrom-Json

    $statementList = Try-Request -Method "Get" -Uri "$BaseUrl/api/v1/statements?page=1&pageSize=10"
    Add-Section -OutputPath $reportPath -Title "GET /api/v1/statements" -Content $statementList.Data

    $statementDetail = Try-Request -Method "Get" -Uri "$BaseUrl/api/v1/statements/$($statementObject.id)"
    Add-Section -OutputPath $reportPath -Title "GET /api/v1/statements/{id}" -Content $statementDetail.Data

    $summary = @(
        "POST /api/v1/statements succeeded: $($statementCreate.Success)",
        "GET /api/v1/statements succeeded: $($statementList.Success)",
        "GET /api/v1/statements/{id} succeeded: $($statementDetail.Success)"
    ) -join [Environment]::NewLine

    Add-Section -OutputPath $reportPath -Title "SUMMARY" -Content $summary

    Write-Host ""
    Write-Host "Statement route repair and smoke test completed." -ForegroundColor Green
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