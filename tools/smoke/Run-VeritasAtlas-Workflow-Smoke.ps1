param(
    [Parameter(Mandatory = $true)]
    [string]$RootDir,
    [string]$BaseUrl = "http://localhost:5209",
    [string]$CaseId = "11111111-1111-1111-1111-111111111111",
    [string]$ContradictionId = "22222222-2222-2222-2222-222222222222",
    [string]$ReviewId = "33333333-3333-3333-3333-333333333333",
    [string]$ClaimId = "44444444-4444-4444-4444-444444444444"
)

$ErrorActionPreference = "Stop"

function Ensure-Dir {
    param([string]$Path)
    if (-not (Test-Path $Path)) {
        New-Item -ItemType Directory -Path $Path -Force | Out-Null
    }
}

$timestamp = Get-Date -Format "yyyyMMdd-HHmmss"
$diag = Join-Path $RootDir "_diagnostics\workflow-smoke-$timestamp"
Ensure-Dir $diag
$report = Join-Path $diag "workflow-smoke-report.md"
$apiLog = Join-Path $diag "api.log"

Set-Content -Path $report -Value "# Workflow Smoke Report`r`n" -Encoding UTF8
Add-Content -Path $report -Value ("Generated: " + (Get-Date -Format "yyyy-MM-dd HH:mm:ss"))
Add-Content -Path $report -Value ""

$apiProcess = $null
Push-Location (Join-Path $RootDir "apps\api\VeritasAtlas.Api")
try {
    $apiProcess = Start-Process "dotnet" -ArgumentList "run" -RedirectStandardOutput $apiLog -RedirectStandardError $apiLog -PassThru
    Start-Sleep -Seconds 8

    $checks = @(
        @{ Name = "Workflow Summary"; Method = "GET"; Url = "$BaseUrl/api/v1/workflow-diagnostics/summary" },
        @{ Name = "Workflow Routes"; Method = "GET"; Url = "$BaseUrl/api/v1/workflow-diagnostics/routes" },
        @{ Name = "Submit Case"; Method = "POST"; Url = "$BaseUrl/api/v1/actions/cases/$CaseId/submit" },
        @{ Name = "Approve Case"; Method = "POST"; Url = "$BaseUrl/api/v1/actions/cases/$CaseId/approve" },
        @{ Name = "Reject Case"; Method = "POST"; Url = "$BaseUrl/api/v1/actions/cases/$CaseId/reject" },
        @{ Name = "Resolve Contradiction"; Method = "POST"; Url = "$BaseUrl/api/v1/actions/contradictions/$ContradictionId/resolve" },
        @{ Name = "Complete Review"; Method = "POST"; Url = "$BaseUrl/api/v1/actions/reviews/$ReviewId/complete" },
        @{ Name = "Send Claim To Review"; Method = "POST"; Url = "$BaseUrl/api/v1/review-workflow/claims/$ClaimId/send-to-review" },
        @{ Name = "Return Claim For Edit"; Method = "POST"; Url = "$BaseUrl/api/v1/review-workflow/claims/$ClaimId/return-for-edit" },
        @{ Name = "Escalate Contradiction"; Method = "POST"; Url = "$BaseUrl/api/v1/review-workflow/contradictions/$ContradictionId/escalate" },
        @{ Name = "Reopen Review"; Method = "POST"; Url = "$BaseUrl/api/v1/review-workflow/reviews/$ReviewId/reopen" },
        @{ Name = "Prepare Publication"; Method = "POST"; Url = "$BaseUrl/api/v1/publication-workflow/cases/$CaseId/prepare" },
        @{ Name = "Publish Case"; Method = "POST"; Url = "$BaseUrl/api/v1/publication-workflow/cases/$CaseId/publish" },
        @{ Name = "Hold Case"; Method = "POST"; Url = "$BaseUrl/api/v1/publication-workflow/cases/$CaseId/hold" }
    )

    foreach ($check in $checks) {
        try {
            $response = Invoke-WebRequest -Uri $check.Url -Method $check.Method -UseBasicParsing -TimeoutSec 15
            Add-Content -Path $report -Value ("## " + $check.Name)
            Add-Content -Path $report -Value ("- StatusCode: " + $response.StatusCode)
            Add-Content -Path $report -Value ("- Url: " + $check.Url)
            Add-Content -Path $report -Value ""
        }
        catch {
            Add-Content -Path $report -Value ("## " + $check.Name)
            Add-Content -Path $report -Value ("- FAILED: " + $_.Exception.Message)
            Add-Content -Path $report -Value ("- Url: " + $check.Url)
            Add-Content -Path $report -Value ""
        }
    }
}
finally {
    Pop-Location
    if ($apiProcess -and -not $apiProcess.HasExited) {
        Stop-Process -Id $apiProcess.Id -Force
    }
}