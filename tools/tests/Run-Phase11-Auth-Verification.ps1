param(
    [Parameter(Mandatory = $true)]
    [string]$RootDir,
    [string]$BaseUrl = "http://localhost:5209"
)

$ErrorActionPreference = "Stop"

function Ensure-Dir {
    param([string]$Path)
    if (-not (Test-Path $Path)) { New-Item -ItemType Directory -Path $Path -Force | Out-Null }
}

function Add-Result {
    param(
        [string]$ReportPath,
        [string]$Name,
        [bool]$Passed,
        [string]$Detail
    )
    Add-Content -Path $ReportPath -Value ("## " + $Name)
    Add-Content -Path $ReportPath -Value ("- Result: " + ($(if ($Passed) { "PASS" } else { "FAIL" })))
    Add-Content -Path $ReportPath -Value ("- Detail: " + $Detail)
    Add-Content -Path $ReportPath -Value ""
}

function Invoke-Json {
    param(
        [string]$Url,
        [string]$Method = "GET",
        [object]$Body = $null,
        [hashtable]$Headers = @{}
    )

    try {
        $params = @{
            Uri = $Url
            Method = $Method
            Headers = $Headers
            TimeoutSec = 20
        }

        if ($null -ne $Body) {
            $params["ContentType"] = "application/json"
            $params["Body"] = ($Body | ConvertTo-Json)
        }

        $result = Invoke-RestMethod @params
        return @{
            Success = $true
            Data = $result
            Message = "OK"
        }
    }
    catch {
        return @{
            Success = $false
            Data = $null
            Message = $_.Exception.Message
        }
    }
}

$timestamp = Get-Date -Format "yyyyMMdd-HHmmss"
$diag = Join-Path $RootDir "_diagnostics\phase11-tests-$timestamp"
Ensure-Dir $diag
$report = Join-Path $diag "phase11-auth-report.md"
$stdoutLog = Join-Path $diag "api-stdout.log"
$stderrLog = Join-Path $diag "api-stderr.log"

Set-Content -Path $report -Value "# Phase 11 Auth Verification Report`r`n" -Encoding UTF8
Add-Content -Path $report -Value ("Generated: " + (Get-Date -Format "yyyy-MM-dd HH:mm:ss"))
Add-Content -Path $report -Value ""

$apiProcess = $null
$failed = $false

Push-Location (Join-Path $RootDir "apps\api\VeritasAtlas.Api")
try {
    $apiProcess = Start-Process "dotnet" -ArgumentList "run" -RedirectStandardOutput $stdoutLog -RedirectStandardError $stderrLog -PassThru
    Start-Sleep -Seconds 8

    $badLogin = Invoke-Json -Url "$BaseUrl/api/v1/auth/login" -Method POST -Body @{ username = "admin1"; password = "wrong" }
    Add-Result -ReportPath $report -Name "Invalid login rejected" -Passed (-not $badLogin.Success) -Detail $badLogin.Message
    if ($badLogin.Success) { $failed = $true }

    $goodLogin = Invoke-Json -Url "$BaseUrl/api/v1/auth/login" -Method POST -Body @{ username = "admin1"; password = "password123" }
    Add-Result -ReportPath $report -Name "Valid login accepted" -Passed $goodLogin.Success -Detail $goodLogin.Message
    if (-not $goodLogin.Success) { $failed = $true; throw "Valid login failed." }

    $token = $goodLogin.Data.accessToken
    $headers = @{ Authorization = "Bearer $token" }

    $me = Invoke-Json -Url "$BaseUrl/api/v1/auth/me" -Method GET -Headers $headers
    Add-Result -ReportPath $report -Name "Current user endpoint works" -Passed $me.Success -Detail ($(if ($me.Success) { "$($me.Data.username) / $($me.Data.role)" } else { $me.Message }))
    if (-not $me.Success) { $failed = $true }

    $protectedNoToken = Invoke-Json -Url "$BaseUrl/api/v1/actions/seed/lifecycle" -Method POST
    Add-Result -ReportPath $report -Name "Protected workflow blocked without token" -Passed (-not $protectedNoToken.Success) -Detail $protectedNoToken.Message
    if ($protectedNoToken.Success) { $failed = $true }

    $seed = Invoke-Json -Url "$BaseUrl/api/v1/actions/seed/lifecycle" -Method POST -Headers $headers
    Add-Result -ReportPath $report -Name "Protected workflow allowed with token" -Passed $seed.Success -Detail $seed.Message
    if (-not $seed.Success) { $failed = $true }

    $audit = Invoke-Json -Url "$BaseUrl/api/v1/workflow-audit/entries" -Method GET -Headers $headers
    Add-Result -ReportPath $report -Name "Workflow audit protected endpoint works" -Passed $audit.Success -Detail $audit.Message
    if (-not $audit.Success) { $failed = $true }
}
finally {
    Pop-Location
    if ($apiProcess -and -not $apiProcess.HasExited) {
        Stop-Process -Id $apiProcess.Id -Force
    }
}

if ($failed) {
    Write-Error "Phase 11 verification failed. See report: $report"
    exit 1
}
else {
    Write-Host "Phase 11 verification passed. Report: $report" -ForegroundColor Green
}