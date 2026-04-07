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
        [string]$Role = "operator"
    )

    $headers = @{ "X-Role" = $Role }

    try {
        $response = Invoke-RestMethod -Uri $Url -Method $Method -Headers $headers -TimeoutSec 20
        return @{
            Success = $true
            Data = $response
            Message = "OK"
        }
    }
    catch {
        $message = $_.Exception.Message
        return @{
            Success = $false
            Data = $null
            Message = $message
        }
    }
}

$timestamp = Get-Date -Format "yyyyMMdd-HHmmss"
$diag = Join-Path $RootDir "_diagnostics\phase9-tests-$timestamp"
Ensure-Dir $diag
$report = Join-Path $diag "phase9-verification-report.md"
$apiLog = Join-Path $diag "api.log"

Set-Content -Path $report -Value "# Phase 9 Verification Report`r`n" -Encoding UTF8
Add-Content -Path $report -Value ("Generated: " + (Get-Date -Format "yyyy-MM-dd HH:mm:ss"))
Add-Content -Path $report -Value ""

$apiProcess = $null
$failed = $false

Push-Location (Join-Path $RootDir "apps\api\VeritasAtlas.Api")
try {
    $apiProcess = Start-Process "dotnet" -ArgumentList "run" -RedirectStandardOutput $apiLog -RedirectStandardError $apiLog -PassThru
    Start-Sleep -Seconds 8

    Invoke-Api -Url "$BaseUrl/api/v1/workflow-audit/clear" -Method POST -Role "admin" | Out-Null

    $seed = Invoke-Api -Url "$BaseUrl/api/v1/actions/seed/lifecycle" -Method POST -Role "admin"
    Add-Result -ReportPath $report -Name "Seed Lifecycle" -Passed $seed.Success -Detail $seed.Message
    if (-not $seed.Success) { $failed = $true; throw "Seeding failed." }

    $caseId = $seed.Data.caseId
    $claimId = $seed.Data.primaryClaimId
    $contradictionId = $seed.Data.contradictionId

    $submit = Invoke-Api -Url "$BaseUrl/api/v1/actions/cases/$caseId/submit" -Method POST -Role "operator"
    Add-Result -ReportPath $report -Name "Submit Case as Operator" -Passed $submit.Success -Detail $submit.Message
    if (-not $submit.Success) { $failed = $true }

    $approveForbidden = Invoke-Api -Url "$BaseUrl/api/v1/actions/cases/$caseId/approve" -Method POST -Role "operator"
    $approveForbiddenPassed = -not $approveForbidden.Success
    Add-Result -ReportPath $report -Name "Approve Case forbidden for Operator" -Passed $approveForbiddenPassed -Detail $approveForbidden.Message
    if (-not $approveForbiddenPassed) { $failed = $true }

    $prepare = Invoke-Api -Url "$BaseUrl/api/v1/publication-workflow/cases/$caseId/prepare" -Method POST -Role "reviewer"
    Add-Result -ReportPath $report -Name "Prepare Publication as Reviewer" -Passed $prepare.Success -Detail $prepare.Message
    if (-not $prepare.Success) { $failed = $true }

    $publishForbidden = Invoke-Api -Url "$BaseUrl/api/v1/publication-workflow/cases/$caseId/publish" -Method POST -Role "reviewer"
    $publishForbiddenPassed = -not $publishForbidden.Success
    Add-Result -ReportPath $report -Name "Publish Case forbidden for Reviewer" -Passed $publishForbiddenPassed -Detail $publishForbidden.Message
    if (-not $publishForbiddenPassed) { $failed = $true }

    $publish = Invoke-Api -Url "$BaseUrl/api/v1/publication-workflow/cases/$caseId/publish" -Method POST -Role "publisher"
    Add-Result -ReportPath $report -Name "Publish Case as Publisher" -Passed $publish.Success -Detail $publish.Message
    if (-not $publish.Success) { $failed = $true }

    $publishAgain = Invoke-Api -Url "$BaseUrl/api/v1/publication-workflow/cases/$caseId/publish" -Method POST -Role "publisher"
    $publishAgainPassed = -not $publishAgain.Success
    Add-Result -ReportPath $report -Name "Invalid repeat publish blocked" -Passed $publishAgainPassed -Detail $publishAgain.Message
    if (-not $publishAgainPassed) { $failed = $true }

    $claimReview = Invoke-Api -Url "$BaseUrl/api/v1/review-workflow/claims/$claimId/send-to-review" -Method POST -Role "reviewer"
    Add-Result -ReportPath $report -Name "Send Claim To Review" -Passed $claimReview.Success -Detail $claimReview.Message
    if (-not $claimReview.Success) { $failed = $true }

    $claimReturn = Invoke-Api -Url "$BaseUrl/api/v1/review-workflow/claims/$claimId/return-for-edit" -Method POST -Role "reviewer"
    Add-Result -ReportPath $report -Name "Return Claim For Edit" -Passed $claimReturn.Success -Detail $claimReturn.Message
    if (-not $claimReturn.Success) { $failed = $true }

    $contradictionEscalate = Invoke-Api -Url "$BaseUrl/api/v1/review-workflow/contradictions/$contradictionId/escalate" -Method POST -Role "reviewer"
    Add-Result -ReportPath $report -Name "Escalate Contradiction" -Passed $contradictionEscalate.Success -Detail $contradictionEscalate.Message
    if (-not $contradictionEscalate.Success) { $failed = $true }

    $contradictionResolve = Invoke-Api -Url "$BaseUrl/api/v1/actions/contradictions/$contradictionId/resolve" -Method POST -Role "reviewer"
    Add-Result -ReportPath $report -Name "Resolve Contradiction" -Passed $contradictionResolve.Success -Detail $contradictionResolve.Message
    if (-not $contradictionResolve.Success) { $failed = $true }

    $audit = Invoke-Api -Url "$BaseUrl/api/v1/workflow-audit/entries" -Method GET -Role "admin"
    $auditPassed = $audit.Success -and $audit.Data.Count -ge 6
    Add-Result -ReportPath $report -Name "Workflow Audit has entries" -Passed $auditPassed -Detail ($(if ($audit.Success) { "Entry count: " + $audit.Data.Count } else { $audit.Message }))
    if (-not $auditPassed) { $failed = $true }

    $rules = Invoke-Api -Url "$BaseUrl/api/v1/workflow-audit/rules" -Method GET -Role "admin"
    $rulesPassed = $rules.Success
    Add-Result -ReportPath $report -Name "Workflow Rules available" -Passed $rulesPassed -Detail $rules.Message
    if (-not $rulesPassed) { $failed = $true }
}
finally {
    Pop-Location
    if ($apiProcess -and -not $apiProcess.HasExited) {
        Stop-Process -Id $apiProcess.Id -Force
    }
}

if ($failed) {
    Write-Error "Phase 9 verification failed. See report: $report"
    exit 1
}
else {
    Write-Host "Phase 9 verification passed. Report: $report" -ForegroundColor Green
}