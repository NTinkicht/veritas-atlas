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

function Invoke-JsonRequestDetailed {
    param(
        [string]$Method,
        [string]$Uri,
        [string]$BodyJson
    )

    try {
        $response = Invoke-WebRequest `
            -Method $Method `
            -Uri $Uri `
            -ContentType "application/json" `
            -Body $BodyJson `
            -UseBasicParsing

        return @{
            Success = $true
            StatusCode = [int]$response.StatusCode
            Body = $response.Content
        }
    }
    catch {
        $statusCode = ""
        $body = ""
        $message = $_.Exception.Message

        if ($_.Exception.Response -ne $null) {
            try {
                $statusCode = [int]$_.Exception.Response.StatusCode
            }
            catch {
                $statusCode = "unknown"
            }

            try {
                $stream = $_.Exception.Response.GetResponseStream()
                if ($stream -ne $null) {
                    $reader = New-Object System.IO.StreamReader($stream)
                    $body = $reader.ReadToEnd()
                    $reader.Dispose()
                }
            }
            catch {
                $body = "[unable to read response body]"
            }
        }

        return @{
            Success = $false
            StatusCode = $statusCode
            Body = $body
            Error = $message
        }
    }
}

$solutionPath = Join-Path $RootDir "VeritasAtlas.slnx"
$apiProjectPath = Join-Path $RootDir $ApiProject

$controllerPath = Join-Path $RootDir "apps\api\VeritasAtlas.Api\Controllers\StatementsController.cs"
$contractsPath = Join-Path $RootDir "apps\api\VeritasAtlas.Api\Contracts\StatementsContracts.cs"
$interfacePath = Join-Path $RootDir "apps\api\VeritasAtlas.Application\Interfaces\IStatementService.cs"
$servicePath = Join-Path $RootDir "apps\api\VeritasAtlas.Infrastructure\Services\StatementService.cs"

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

$commitMessage = "checkpoint before statement endpoint diagnostics - $(Get-Date -Format 'yyyy-MM-dd HH:mm:ss')"
git commit -m $commitMessage 2>$null
if ($LASTEXITCODE -ne 0) {
    Write-Host "No new commit created. Continuing with diagnostics." -ForegroundColor Yellow
}
else {
    Write-Host "Created git commit: $commitMessage" -ForegroundColor Green
}

$timestamp = Get-Date -Format "yyyyMMdd-HHmmss"
$diagDir = Join-Path $RootDir "_diagnostics\statement-endpoint-diagnostics-$timestamp"
Ensure-Directory $diagDir

$reportPath = Join-Path $diagDir "statement-endpoint-diagnostics-report.txt"
$stdoutPath = Join-Path $diagDir "api-stdout.log"
$stderrPath = Join-Path $diagDir "api-stderr.log"

Set-Content -Path $reportPath -Value "Statement endpoint diagnostics`r`nGenerated: $(Get-Date -Format s)`r`nRoot: $RootDir" -Encoding UTF8

Add-Section -OutputPath $reportPath -Title "FILE: StatementsController.cs" -Content (Get-Content $controllerPath -Raw)
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

    $statementBody = @{
        evidenceId = "00000000-0000-0000-0000-000000000001"
        text = "Diagnostic statement probe"
        createdBy = "statement-diagnostics"
    } | ConvertTo-Json -Depth 5

    $probeV1 = Invoke-JsonRequestDetailed -Method "Post" -Uri "$BaseUrl/api/v1/statements" -BodyJson $statementBody
    Add-Section -OutputPath $reportPath -Title "POST /api/v1/statements" -Content (($probeV1 | ConvertTo-Json -Depth 10))

    $probePlain = Invoke-JsonRequestDetailed -Method "Post" -Uri "$BaseUrl/api/statements" -BodyJson $statementBody
    Add-Section -OutputPath $reportPath -Title "POST /api/statements" -Content (($probePlain | ConvertTo-Json -Depth 10))

    Add-Section -OutputPath $reportPath -Title "API STDOUT" -Content (if (Test-Path $stdoutPath) { Get-Content $stdoutPath -Raw } else { "[missing]" })
    Add-Section -OutputPath $reportPath -Title "API STDERR" -Content (if (Test-Path $stderrPath) { Get-Content $stderrPath -Raw } else { "[missing]" })

    Write-Host ""
    Write-Host "Diagnostics completed." -ForegroundColor Green
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