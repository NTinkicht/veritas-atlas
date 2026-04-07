param(
    [Parameter(Mandatory = $true)]
    [string]$RootDir,
    [string]$BaseUrl = "http://localhost:5091"
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
$diag = Join-Path $RootDir "_diagnostics\phase11_1-tests-$timestamp"
Ensure-Dir $diag
$report = Join-Path $diag "phase11_1-auth-debug-report.md"
$stdoutLog = Join-Path $diag "api-stdout.log"
$stderrLog = Join-Path $diag "api-stderr.log"

Set-Content -Path $report -Value "# Phase 11.1 Auth Debug Verification Report`r`n" -Encoding UTF8
Add-Content -Path $report -Value ("Generated: " + (Get-Date -Format "yyyy-MM-dd HH:mm:ss"))
Add-Content -Path $report -Value ("BaseUrl: " + $BaseUrl)
Add-Content -Path $report -Value ""

$apiProcess = $null
$failed = $false

Push-Location (Join-Path $RootDir "apps\api\VeritasAtlas.Api")
try {
    $env:ASPNETCORE_URLS = $BaseUrl
    $apiProcess = Start-Process "dotnet" -ArgumentList "run" -RedirectStandardOutput $stdoutLog -RedirectStandardError $stderrLog -PassThru
    Start-Sleep -Seconds 8

    $publicDiag = Invoke-Json -Url "$BaseUrl/api/v1/auth-diagnostics/public"
    Add-Result -ReportPath $report -Name "Public diagnostics endpoint works" -Passed $publicDiag.Success -Detail $(if ($publicDiag.Success) { "Issuer=$($publicDiag.Data.jwtIssuer), Audience=$($publicDiag.Data.jwtAudience)" } else { $publicDiag.Message })
    if (-not $publicDiag.Success) { $failed = $true }

    $badLogin = Invoke-Json -Url "$BaseUrl/api/v1/auth/login" -Method POST -Body @{ username = "admin1"; password = "wrong" }
    Add-Result -ReportPath $report -Name "Invalid login rejected" -Passed (-not $badLogin.Success) -Detail $badLogin.Message
    if ($badLogin.Success) { $failed = $true }

    $goodLogin = Invoke-Json -Url "$BaseUrl/api/v1/auth/login" -Method POST -Body @{ username = "admin1"; password = "password123" }
    Add-Result -ReportPath $report -Name "Valid login accepted" -Passed $goodLogin.Success -Detail $goodLogin.Message
    if (-not $goodLogin.Success) { $failed = $true; throw "Valid login failed." }

    $token = $goodLogin.Data.accessToken
    $headers = @{ Authorization = "Bearer $token" }

    $me = Invoke-Json -Url "$BaseUrl/api/v1/auth/me" -Method GET -Headers $headers
    Add-Result -ReportPath $report -Name "Auth me works" -Passed $me.Success -Detail $(if ($me.Success) { "$($me.Data.username) / $($me.Data.role)" } else { $me.Message })
    if (-not $me.Success) { $failed = $true }

    $protectedDiag = Invoke-Json -Url "$BaseUrl/api/v1/auth-diagnostics/protected" -Method GET -Headers $headers
    Add-Result -ReportPath $report -Name "Protected diagnostics works" -Passed $protectedDiag.Success -Detail $(if ($protectedDiag.Success) { "Claims=$($protectedDiag.Data.claims.Count)" } else { $protectedDiag.Message })
    if (-not $protectedDiag.Success) { $failed = $true }

    $seed = Invoke-Json -Url "$BaseUrl/api/v1/actions/seed/lifecycle" -Method POST -Headers $headers
    Add-Result -ReportPath $report -Name "Protected workflow accepts token" -Passed $seed.Success -Detail $seed.Message
    if (-not $seed.Success) { $failed = $true }

    $audit = Invoke-Json -Url "$BaseUrl/api/v1/workflow-audit/entries" -Method GET -Headers $headers
    Add-Result -ReportPath $report -Name "Protected audit accepts token" -Passed $audit.Success -Detail $audit.Message
    if (-not $audit.Success) { $failed = $true }
}
finally {
    Pop-Location
    if ($apiProcess -and -not $apiProcess.HasExited) {
        Stop-Process -Id $apiProcess.Id -Force
    }
}

if ($failed) {
    Write-Error "Phase 11.1 auth debug verification failed. See report: $report"
    exit 1
}
else {
    Write-Host "Phase 11.1 auth debug verification passed. Report: $report" -ForegroundColor Green
}