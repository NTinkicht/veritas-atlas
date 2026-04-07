param(
    [Parameter(Mandatory = $true)]
    [string]$RootDir
)

$ErrorActionPreference = "Stop"

function Ensure-Dir {
    param([string]$Path)
    if ([string]::IsNullOrWhiteSpace($Path)) {
        throw "Ensure-Dir received an empty path."
    }
    if (-not (Test-Path $Path)) {
        New-Item -ItemType Directory -Path $Path -Force | Out-Null
    }
}

function Write-File {
    param(
        [string]$Path,
        [string]$Content
    )
    if ([string]::IsNullOrWhiteSpace($Path)) {
        throw "Write-File received an empty path."
    }
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
            if ($LASTEXITCODE -eq 0) {
                Write-Host "Created git commit: $Message"
            }
            else {
                Write-Host "No new commit created. Continuing."
            }
        }
    }
    finally {
        Pop-Location
    }
}

function Build-All {
    param([string]$RootDir)

    Push-Location $RootDir
    try {
        dotnet build
        if ($LASTEXITCODE -ne 0) { throw "Backend failed" }
    }
    finally {
        Pop-Location
    }

    $web = Join-Path $RootDir "apps\web\veritas-atlas-web"
    Push-Location $web
    try {
        npm run build
        if ($LASTEXITCODE -ne 0) { throw "Frontend failed" }
    }
    finally {
        Pop-Location
    }
}

function Ensure-ImportLine {
    param(
        [string]$Content,
        [string]$Anchor,
        [string]$ImportLine
    )

    if ($Content -match [regex]::Escape($ImportLine)) {
        return $Content
    }

    return $Content -replace [regex]::Escape($Anchor), ($Anchor + [Environment]::NewLine + $ImportLine)
}

function Ensure-NavBlock {
    param(
        [string]$Content,
        [string]$Anchor,
        [string]$NavBlock,
        [string]$PresencePattern
    )

    if ($Content -match $PresencePattern) {
        return $Content
    }

    return $Content -replace [regex]::Escape($Anchor), ($Anchor + [Environment]::NewLine + $NavBlock)
}

function Ensure-RouteBlock {
    param(
        [string]$Content,
        [string]$AnchorRoute,
        [string]$RouteBlock,
        [string]$PresencePattern
    )

    if ($Content -match $PresencePattern) {
        return $Content
    }

    return $Content -replace [regex]::Escape($AnchorRoute), ($AnchorRoute + [Environment]::NewLine + $RouteBlock)
}

Write-Host "Checkpointing current code with git..." -ForegroundColor Cyan
Git-Checkpoint -Message ("checkpoint before phase 7.4-7.8 bundle - " + (Get-Date -Format "yyyy-MM-dd HH:mm:ss"))

Write-Host "Applying bundled phases 7.4 to 7.8..." -ForegroundColor Cyan

$api = Join-Path $RootDir "apps\api\VeritasAtlas.Api"
$web = Join-Path $RootDir "apps\web\veritas-atlas-web\src"
$diag = Join-Path $RootDir "_diagnostics\phase-7-4-to-7-8"
Ensure-Dir $diag

# =========================
# 7.4 Review action endpoints
# =========================
Write-File (Join-Path $api "Controllers\ReviewWorkflowController.cs") @'
using Microsoft.AspNetCore.Mvc;

namespace VeritasAtlas.Api.Controllers;

[ApiController]
[Route("api/v1/review-workflow")]
public class ReviewWorkflowController : ControllerBase
{
    [HttpPost("claims/{claimId}/send-to-review")]
    public IActionResult SendClaimToReview(Guid claimId)
    {
        return Ok(new { ClaimId = claimId, Status = "InReview", Timestamp = DateTime.UtcNow });
    }

    [HttpPost("claims/{claimId}/return-for-edit")]
    public IActionResult ReturnClaimForEdit(Guid claimId)
    {
        return Ok(new { ClaimId = claimId, Status = "NeedsEdit", Timestamp = DateTime.UtcNow });
    }

    [HttpPost("contradictions/{id}/escalate")]
    public IActionResult EscalateContradiction(Guid id)
    {
        return Ok(new { ContradictionId = id, Status = "Escalated", Timestamp = DateTime.UtcNow });
    }

    [HttpPost("reviews/{id}/reopen")]
    public IActionResult ReopenReview(Guid id)
    {
        return Ok(new { ReviewId = id, Status = "Reopened", Timestamp = DateTime.UtcNow });
    }
}
'@

# =========================
# 7.5 Publication action endpoints
# =========================
Write-File (Join-Path $api "Controllers\PublicationWorkflowController.cs") @'
using Microsoft.AspNetCore.Mvc;

namespace VeritasAtlas.Api.Controllers;

[ApiController]
[Route("api/v1/publication-workflow")]
public class PublicationWorkflowController : ControllerBase
{
    [HttpPost("cases/{caseId}/prepare")]
    public IActionResult PreparePublication(Guid caseId)
    {
        return Ok(new { CaseId = caseId, Status = "PreparedForPublication", Timestamp = DateTime.UtcNow });
    }

    [HttpPost("cases/{caseId}/publish")]
    public IActionResult PublishCase(Guid caseId)
    {
        return Ok(new { CaseId = caseId, Status = "Published", Timestamp = DateTime.UtcNow });
    }

    [HttpPost("cases/{caseId}/hold")]
    public IActionResult HoldCase(Guid caseId)
    {
        return Ok(new { CaseId = caseId, Status = "PublicationHold", Timestamp = DateTime.UtcNow });
    }
}
'@

# =========================
# 7.6 Frontend workflow APIs + hooks
# =========================
Write-File (Join-Path $web "api\workflowActions.ts") @'
export type WorkflowActionResponse = {
  claimId?: string;
  contradictionId?: string;
  reviewId?: string;
  caseId?: string;
  status: string;
  timestamp: string;
};

async function postAction(url: string): Promise<WorkflowActionResponse> {
  const response = await fetch(url, { method: "POST" });

  if (!response.ok) {
    const text = await response.text();
    throw new Error(text || `HTTP ${response.status}`);
  }

  return response.json() as Promise<WorkflowActionResponse>;
}

export function sendClaimToReview(claimId: string) {
  return postAction(`/api/v1/review-workflow/claims/${claimId}/send-to-review`);
}

export function returnClaimForEdit(claimId: string) {
  return postAction(`/api/v1/review-workflow/claims/${claimId}/return-for-edit`);
}

export function escalateContradiction(contradictionId: string) {
  return postAction(`/api/v1/review-workflow/contradictions/${contradictionId}/escalate`);
}

export function reopenReview(reviewId: string) {
  return postAction(`/api/v1/review-workflow/reviews/${reviewId}/reopen`);
}

export function preparePublication(caseId: string) {
  return postAction(`/api/v1/publication-workflow/cases/${caseId}/prepare`);
}

export function publishCase(caseId: string) {
  return postAction(`/api/v1/publication-workflow/cases/${caseId}/publish`);
}

export function holdCase(caseId: string) {
  return postAction(`/api/v1/publication-workflow/cases/${caseId}/hold`);
}
'@

Write-File (Join-Path $web "hooks\useWorkflowActions.ts") @'
import { useMutation } from "@tanstack/react-query";
import {
  sendClaimToReview,
  returnClaimForEdit,
  escalateContradiction,
  reopenReview,
  preparePublication,
  publishCase,
  holdCase,
} from "../api/workflowActions";

export function useSendClaimToReviewAction() {
  return useMutation({
    mutationFn: (claimId: string) => sendClaimToReview(claimId),
  });
}

export function useReturnClaimForEditAction() {
  return useMutation({
    mutationFn: (claimId: string) => returnClaimForEdit(claimId),
  });
}

export function useEscalateContradictionAction() {
  return useMutation({
    mutationFn: (contradictionId: string) => escalateContradiction(contradictionId),
  });
}

export function useReopenReviewAction() {
  return useMutation({
    mutationFn: (reviewId: string) => reopenReview(reviewId),
  });
}

export function usePreparePublicationAction() {
  return useMutation({
    mutationFn: (caseId: string) => preparePublication(caseId),
  });
}

export function usePublishCaseAction() {
  return useMutation({
    mutationFn: (caseId: string) => publishCase(caseId),
  });
}

export function useHoldCaseAction() {
  return useMutation({
    mutationFn: (caseId: string) => holdCase(caseId),
  });
}
'@

Write-File (Join-Path $web "components\WorkflowActionPanel.tsx") @'
type WorkflowActionButton = {
  label: string;
  onClick: () => void;
  disabled?: boolean;
};

export function WorkflowActionPanel({
  title,
  buttons,
  message,
}: {
  title: string;
  buttons: WorkflowActionButton[];
  message?: string;
}) {
  return (
    <div style={panelStyle}>
      <h3 style={{ marginTop: 0 }}>{title}</h3>
      <div style={{ display: "flex", gap: 12, flexWrap: "wrap" }}>
        {buttons.map((button) => (
          <button
            key={button.label}
            onClick={button.onClick}
            disabled={button.disabled}
            style={buttonStyle}
          >
            {button.label}
          </button>
        ))}
      </div>
      {message && <p style={{ marginTop: 12, marginBottom: 0 }}>{message}</p>}
    </div>
  );
}

const panelStyle: React.CSSProperties = {
  border: "1px solid #ddd",
  borderRadius: 14,
  padding: 16,
};

const buttonStyle: React.CSSProperties = {
  border: "1px solid #bbb",
  borderRadius: 10,
  padding: "10px 14px",
  background: "white",
  cursor: "pointer",
  font: "inherit",
};
'@

# =========================
# 7.7 Operational workflow console page
# =========================
Write-File (Join-Path $web "pages\WorkflowConsolePage.tsx") @'
import { useState } from "react";
import { WorkflowActionPanel } from "../components/WorkflowActionPanel";
import {
  useEscalateContradictionAction,
  useHoldCaseAction,
  usePreparePublicationAction,
  usePublishCaseAction,
  useReopenReviewAction,
  useReturnClaimForEditAction,
  useSendClaimToReviewAction,
} from "../hooks/useWorkflowActions";

export function WorkflowConsolePage() {
  const [claimId, setClaimId] = useState("");
  const [caseId, setCaseId] = useState("");
  const [contradictionId, setContradictionId] = useState("");
  const [reviewId, setReviewId] = useState("");

  const sendClaim = useSendClaimToReviewAction();
  const returnClaim = useReturnClaimForEditAction();
  const escalate = useEscalateContradictionAction();
  const reopen = useReopenReviewAction();
  const prepare = usePreparePublicationAction();
  const publish = usePublishCaseAction();
  const hold = useHoldCaseAction();

  return (
    <div style={{ fontFamily: "Arial, sans-serif", padding: 24 }}>
      <h1 style={{ marginTop: 0 }}>Workflow Console</h1>
      <p style={{ color: "#555" }}>
        Console for review routing, contradiction escalation, and publication workflow actions.
      </p>

      <div style={inputGridStyle}>
        <label style={labelStyle}>
          Claim Id
          <input value={claimId} onChange={(e) => setClaimId(e.target.value)} style={inputStyle} />
        </label>
        <label style={labelStyle}>
          Case Id
          <input value={caseId} onChange={(e) => setCaseId(e.target.value)} style={inputStyle} />
        </label>
        <label style={labelStyle}>
          Contradiction Id
          <input value={contradictionId} onChange={(e) => setContradictionId(e.target.value)} style={inputStyle} />
        </label>
        <label style={labelStyle}>
          Review Id
          <input value={reviewId} onChange={(e) => setReviewId(e.target.value)} style={inputStyle} />
        </label>
      </div>

      <div style={{ display: "grid", gridTemplateColumns: "1fr 1fr", gap: 16, marginTop: 20 }}>
        <WorkflowActionPanel
          title="Review Workflow"
          buttons={[
            { label: "Send Claim To Review", onClick: () => sendClaim.mutate(claimId), disabled: !claimId },
            { label: "Return Claim For Edit", onClick: () => returnClaim.mutate(claimId), disabled: !claimId },
            { label: "Escalate Contradiction", onClick: () => escalate.mutate(contradictionId), disabled: !contradictionId },
            { label: "Reopen Review", onClick: () => reopen.mutate(reviewId), disabled: !reviewId },
          ]}
          message={sendClaim.data?.status || returnClaim.data?.status || escalate.data?.status || reopen.data?.status}
        />

        <WorkflowActionPanel
          title="Publication Workflow"
          buttons={[
            { label: "Prepare Publication", onClick: () => prepare.mutate(caseId), disabled: !caseId },
            { label: "Publish Case", onClick: () => publish.mutate(caseId), disabled: !caseId },
            { label: "Hold Case", onClick: () => hold.mutate(caseId), disabled: !caseId },
          ]}
          message={prepare.data?.status || publish.data?.status || hold.data?.status}
        />
      </div>
    </div>
  );
}

const inputGridStyle: React.CSSProperties = {
  display: "grid",
  gridTemplateColumns: "1fr 1fr",
  gap: 12,
};

const labelStyle: React.CSSProperties = {
  display: "grid",
  gap: 6,
};

const inputStyle: React.CSSProperties = {
  border: "1px solid #ccc",
  borderRadius: 10,
  padding: "10px 12px",
  font: "inherit",
};
'@

# =========================
# 7.8 Route/nav integration + report
# =========================
$main = Join-Path $web "main.tsx"
$content = Get-Content $main -Raw

if ($content -notmatch 'import \{ WorkflowConsolePage \} from "\./pages/WorkflowConsolePage";') {
    $content = $content -replace 'import \{ DashboardPage \} from "\./pages/DashboardPage";', @'
import { DashboardPage } from "./pages/DashboardPage";
import { WorkflowConsolePage } from "./pages/WorkflowConsolePage";
'@
}

$content = Ensure-RouteBlock -Content $content -AnchorRoute '{ path: "/dashboard", element: <DashboardPage /> },' -RouteBlock '{ path: "/workflow-console", element: <WorkflowConsolePage /> },' -PresencePattern 'path: "/workflow-console"'
$content = Ensure-NavBlock -Content $content -Anchor '<Link to="/operational-actions">Operational Actions</Link>' -NavBlock '<Link to="/workflow-console">Workflow Console</Link>' -PresencePattern 'to="/workflow-console"'

Write-File $main $content

Write-File (Join-Path $diag "phase-7-4-to-7-8-summary.md") @'
# Phase 7.4 to 7.8 Bundle Summary

## Included
- Review workflow backend action endpoints
- Publication workflow backend action endpoints
- Frontend workflow action API and hooks
- Reusable workflow action panel
- Workflow Console page
- Router and navigation integration

## Purpose
- move beyond passive UI shells
- establish interactive review and publication controls
- create a base for later real service wiring and persistence

## Next likely depth work
- connect action endpoints to application services
- persist state transitions
- validate business rules
- refresh affected queries after mutations
- add seeded end-to-end test scenario
'@

Write-Host "Building..." -ForegroundColor Cyan
Build-All -RootDir $RootDir

Write-Host "Bundled phases 7.4 to 7.8 DONE" -ForegroundColor Green
