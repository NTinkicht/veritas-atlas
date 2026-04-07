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

$timestamp = Get-Date -Format "yyyyMMdd-HHmmss"
$diag = Join-Path $RootDir "_diagnostics\seeded-lifecycle-$timestamp"
Ensure-Dir $diag
$report = Join-Path $diag "seeded-lifecycle-report.md"
$apiLog = Join-Path $diag "api.log"

Set-Content -Path $report -Value "# Seeded Lifecycle Report`r`n" -Encoding UTF8
Add-Content -Path $report -Value ("Generated: " + (Get-Date -Format "yyyy-MM-dd HH:mm:ss"))
Add-Content -Path $report -Value ""

$apiProcess = $null
Push-Location (Join-Path $RootDir "apps\api\VeritasAtlas.Api")
try {
    $apiProcess = Start-Process "dotnet" -ArgumentList "run" -RedirectStandardOutput $apiLog -RedirectStandardError $apiLog -PassThru
    Start-Sleep -Seconds 8

    $seed = Invoke-RestMethod -Uri "$BaseUrl/api/v1/actions/seed/lifecycle" -Method POST
    Add-Content -Path $report -Value "## Seed"
    Add-Content -Path $report -Value ("- CaseId: " + $seed.caseId)
    Add-Content -Path $report -Value ("- ContradictionId: " + $seed.contradictionId)
    Add-Content -Path $report -Value ""

    $steps = @(
        @{ Name = "Submit Case"; Url = "$BaseUrl/api/v1/actions/cases/$($seed.caseId)/submit" },
        @{ Name = "Prepare Publication"; Url = "$BaseUrl/api/v1/publication-workflow/cases/$($seed.caseId)/prepare" },
        @{ Name = "Resolve Contradiction"; Url = "$BaseUrl/api/v1/actions/contradictions/$($seed.contradictionId)/resolve" },
        @{ Name = "Publish Case"; Url = "$BaseUrl/api/v1/publication-workflow/cases/$($seed.caseId)/publish" }
    )

    foreach ($step in $steps) {
        try {
            $response = Invoke-RestMethod -Uri $step.Url -Method POST
            Add-Content -Path $report -Value ("## " + $step.Name)
            Add-Content -Path $report -Value ("- Status: " + $response.status)
            Add-Content -Path $report -Value ("- Timestamp: " + $response.timestampUtc)
            Add-Content -Path $report -Value ""
        }
        catch {
            Add-Content -Path $report -Value ("## " + $step.Name)
            Add-Content -Path $report -Value ("- FAILED: " + $_.Exception.Message)
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