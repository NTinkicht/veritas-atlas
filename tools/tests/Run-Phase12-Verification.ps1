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

function Invoke-Api {
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
$diag = Join-Path $RootDir "_diagnostics\phase12-tests-$timestamp"
Ensure-Dir $diag
$report = Join-Path $diag "phase12-report.md"
$stdoutLog = Join-Path $diag "api-stdout.log"
$stderrLog = Join-Path $diag "api-stderr.log"

Set-Content -Path $report -Value "# Phase 12 Verification Report`r`n" -Encoding UTF8
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

    $login = Invoke-Api -Url "$BaseUrl/api/v1/auth/login" -Method POST -Body @{ username = "admin1"; password = "password123" }
    Add-Result -ReportPath $report -Name "Login works" -Passed $login.Success -Detail $login.Message
    if (-not $login.Success) { $failed = $true; throw "Login failed." }

    $headers = @{ Authorization = "Bearer $($login.Data.accessToken)" }

    $me = Invoke-Api -Url "$BaseUrl/api/v1/auth/me" -Method GET -Headers $headers
    Add-Result -ReportPath $report -Name "Auth me works" -Passed $me.Success -Detail $(if ($me.Success) { "$($me.Data.username) / $($me.Data.role)" } else { $me.Message })
    if (-not $me.Success) { $failed = $true; throw "Auth me failed." }

    $seed = Invoke-Api -Url "$BaseUrl/api/v1/actions/seed/lifecycle" -Method POST -Headers $headers
    Add-Result -ReportPath $report -Name "Seed lifecycle works" -Passed $seed.Success -Detail $seed.Message
    if (-not $seed.Success) { $failed = $true }

    $snapshot = Invoke-Api -Url "$BaseUrl/api/v1/persistence/snapshot" -Method GET -Headers $headers
    $snapshotPass = $snapshot.Success -and $snapshot.Data.exists -eq $true
    Add-Result -ReportPath $report -Name "Snapshot exists after seed" -Passed $snapshotPass -Detail $(if ($snapshot.Success) { "Exists: $($snapshot.Data.exists)" } else { $snapshot.Message })
    if (-not $snapshotPass) { $failed = $true }

    if ($seed.Success) {
        $prepare = Invoke-Api -Url "$BaseUrl/api/v1/publication-workflow/cases/$($seed.Data.caseId)/prepare" -Method POST -Headers $headers
        Add-Result -ReportPath $report -Name "Prepare publication works" -Passed $prepare.Success -Detail $prepare.Message
        if (-not $prepare.Success) { $failed = $true }

        $resolve = Invoke-Api -Url "$BaseUrl/api/v1/actions/contradictions/$($seed.Data.contradictionId)/resolve" -Method POST -Headers $headers
        Add-Result -ReportPath $report -Name "Resolve contradiction works" -Passed $resolve.Success -Detail $resolve.Message
        if (-not $resolve.Success) { $failed = $true }

        $snapshotAfter = Invoke-Api -Url "$BaseUrl/api/v1/persistence/snapshot" -Method GET -Headers $headers
        $snapshotAfterPass = $snapshotAfter.Success -and $snapshotAfter.Data.caseStatus -ne $null -and $snapshotAfter.Data.contradictionStatus -eq "Resolved"
        Add-Result -ReportPath $report -Name "Snapshot updates after transitions" -Passed $snapshotAfterPass -Detail $(if ($snapshotAfter.Success) { "CaseStatus=$($snapshotAfter.Data.caseStatus), ContradictionStatus=$($snapshotAfter.Data.contradictionStatus)" } else { $snapshotAfter.Message })
        if (-not $snapshotAfterPass) { $failed = $true }
    }

    $reset = Invoke-Api -Url "$BaseUrl/api/v1/persistence/reset" -Method POST -Headers $headers
    Add-Result -ReportPath $report -Name "Reset works" -Passed $reset.Success -Detail $reset.Message
    if (-not $reset.Success) { $failed = $true }

    $snapshotCleared = Invoke-Api -Url "$BaseUrl/api/v1/persistence/snapshot" -Method GET -Headers $headers
    $snapshotClearedPass = $snapshotCleared.Success -and $snapshotCleared.Data.exists -eq $false
    Add-Result -ReportPath $report -Name "Snapshot cleared after reset" -Passed $snapshotClearedPass -Detail $(if ($snapshotCleared.Success) { "Exists: $($snapshotCleared.Data.exists)" } else { $snapshotCleared.Message })
    if (-not $snapshotClearedPass) { $failed = $true }

    $audit = Invoke-Api -Url "$BaseUrl/api/v1/workflow-audit/entries" -Method GET -Headers $headers
    Add-Result -ReportPath $report -Name "Audit endpoint accessible" -Passed $audit.Success -Detail $audit.Message
    if (-not $audit.Success) { $failed = $true }
}
finally {
    Pop-Location
    if ($apiProcess -and -not $apiProcess.HasExited) {
        Stop-Process -Id $apiProcess.Id -Force
    }
}

if ($failed) {
    Write-Error "Phase 12 verification failed. See report: $report"
    exit 1
}
else {
    Write-Host "Phase 12 verification passed. Report: $report" -ForegroundColor Green
}