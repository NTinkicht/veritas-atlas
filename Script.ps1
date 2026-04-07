param(
    [Parameter(Mandatory = $true)]
    [string]$RootDir
)

$ErrorActionPreference = "Stop"

function Ensure-Dir {
    param([string]$Path)
    if (-not (Test-Path $Path)) {
        New-Item -ItemType Directory -Path $Path -Force | Out-Null
    }
}

function Write-File {
    param([string]$Path, [string]$Content)
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
        }
    } finally {
        Pop-Location
    }
}

function Build-All {
    param([string]$RootDir)

    Push-Location $RootDir
    dotnet build
    Pop-Location

    $web = Join-Path $RootDir "apps\web\veritas-atlas-web"
    Push-Location $web
    npm run build
    Pop-Location
}

Write-Host "Checkpoint..." -ForegroundColor Cyan
Git-Checkpoint -Message ("checkpoint before phase 7.2 - " + (Get-Date))

Write-Host "Applying Phase 7.2 - API write actions foundation..." -ForegroundColor Cyan

$api = Join-Path $RootDir "apps\api\VeritasAtlas.Api"

# Add minimal POST endpoints for key flows

Write-File (Join-Path $api "Controllers\ActionsController.cs") @'
using Microsoft.AspNetCore.Mvc;

namespace VeritasAtlas.Api.Controllers;

[ApiController]
[Route("api/v1/actions")]
public class ActionsController : ControllerBase
{
    [HttpPost("cases/{caseId}/submit")]
    public IActionResult SubmitCase(Guid caseId)
    {
        return Ok(new { CaseId = caseId, Status = "Submitted", Timestamp = DateTime.UtcNow });
    }

    [HttpPost("cases/{caseId}/approve")]
    public IActionResult ApproveCase(Guid caseId)
    {
        return Ok(new { CaseId = caseId, Status = "Approved", Timestamp = DateTime.UtcNow });
    }

    [HttpPost("cases/{caseId}/reject")]
    public IActionResult RejectCase(Guid caseId)
    {
        return Ok(new { CaseId = caseId, Status = "Rejected", Timestamp = DateTime.UtcNow });
    }

    [HttpPost("contradictions/{id}/resolve")]
    public IActionResult ResolveContradiction(Guid id)
    {
        return Ok(new { ContradictionId = id, Status = "Resolved", Timestamp = DateTime.UtcNow });
    }

    [HttpPost("reviews/{id}/complete")]
    public IActionResult CompleteReview(Guid id)
    {
        return Ok(new { ReviewId = id, Status = "Completed", Timestamp = DateTime.UtcNow });
    }
}
'@

# Diagnostics note
$diag = Join-Path $RootDir "_diagnostics\phase-7-2"
Ensure-Dir $diag

Write-File (Join-Path $diag "phase-7-2-summary.md") @'
# Phase 7.2 Summary

## Added
- Action endpoints (POST)
    - Submit Case
    - Approve Case
    - Reject Case
    - Resolve Contradiction
    - Complete Review

## Purpose
- Introduce write operations
- Prepare UI for real interactions
- Enable future service wiring

## Next
- Connect to Application Layer services
- Add validation
- Add persistence
- Add status transitions
'@

Write-Host "Building..." -ForegroundColor Cyan
Build-All -RootDir $RootDir

Write-Host "Phase 7.2 DONE" -ForegroundColor Green
