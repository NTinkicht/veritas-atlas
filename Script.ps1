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

Write-Host "Checkpointing current code with git..." -ForegroundColor Cyan
Git-Checkpoint -Message ("checkpoint before phase 7.3 - " + (Get-Date -Format "yyyy-MM-dd HH:mm:ss"))

Write-Host "Applying Phase 7.3 - frontend action wiring and operational controls..." -ForegroundColor Cyan

$web = Join-Path $RootDir "apps\web\veritas-atlas-web\src"

Write-File (Join-Path $web "api\actions.ts") @'
export type ActionResponse = {
  caseId?: string;
  contradictionId?: string;
  reviewId?: string;
  status: string;
  timestamp: string;
};

async function postAction(url: string): Promise<ActionResponse> {
  const response = await fetch(url, {
    method: "POST",
  });

  if (!response.ok) {
    const text = await response.text();
    throw new Error(text || `HTTP ${response.status}`);
  }

  return response.json() as Promise<ActionResponse>;
}

export function submitCase(caseId: string) {
  return postAction(`/api/v1/actions/cases/${caseId}/submit`);
}

export function approveCase(caseId: string) {
  return postAction(`/api/v1/actions/cases/${caseId}/approve`);
}

export function rejectCase(caseId: string) {
  return postAction(`/api/v1/actions/cases/${caseId}/reject`);
}

export function resolveContradiction(contradictionId: string) {
  return postAction(`/api/v1/actions/contradictions/${contradictionId}/resolve`);
}

export function completeReview(reviewId: string) {
  return postAction(`/api/v1/actions/reviews/${reviewId}/complete`);
}
'@

Write-File (Join-Path $web "hooks\useActionMutations.ts") @'
import { useMutation } from "@tanstack/react-query";
import {
  approveCase,
  completeReview,
  rejectCase,
  resolveContradiction,
  submitCase,
} from "../api/actions";

export function useSubmitCaseAction() {
  return useMutation({
    mutationFn: (caseId: string) => submitCase(caseId),
  });
}

export function useApproveCaseAction() {
  return useMutation({
    mutationFn: (caseId: string) => approveCase(caseId),
  });
}

export function useRejectCaseAction() {
  return useMutation({
    mutationFn: (caseId: string) => rejectCase(caseId),
  });
}

export function useResolveContradictionAction() {
  return useMutation({
    mutationFn: (contradictionId: string) => resolveContradiction(contradictionId),
  });
}

export function useCompleteReviewAction() {
  return useMutation({
    mutationFn: (reviewId: string) => completeReview(reviewId),
  });
}
'@

Write-File (Join-Path $web "components\ActionButtonsPanel.tsx") @'
type ActionButtonItem = {
  label: string;
  onClick: () => void;
  disabled?: boolean;
};

export function ActionButtonsPanel({
  title,
  items,
  message,
}: {
  title: string;
  items: ActionButtonItem[];
  message?: string;
}) {
  return (
    <div style={panelStyle}>
      <h3 style={{ marginTop: 0 }}>{title}</h3>
      <div style={{ display: "flex", gap: 12, flexWrap: "wrap" }}>
        {items.map((item) => (
          <button
            key={item.label}
            onClick={item.onClick}
            disabled={item.disabled}
            style={buttonStyle}
          >
            {item.label}
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

Write-File (Join-Path $web "pages\OperationalActionsPage.tsx") @'
import { useState } from "react";
import { ActionButtonsPanel } from "../components/ActionButtonsPanel";
import {
  useApproveCaseAction,
  useCompleteReviewAction,
  useRejectCaseAction,
  useResolveContradictionAction,
  useSubmitCaseAction,
} from "../hooks/useActionMutations";

export function OperationalActionsPage() {
  const [caseId, setCaseId] = useState("");
  const [contradictionId, setContradictionId] = useState("");
  const [reviewId, setReviewId] = useState("");

  const submitCaseAction = useSubmitCaseAction();
  const approveCaseAction = useApproveCaseAction();
  const rejectCaseAction = useRejectCaseAction();
  const resolveContradictionAction = useResolveContradictionAction();
  const completeReviewAction = useCompleteReviewAction();

  return (
    <div style={{ fontFamily: "Arial, sans-serif", padding: 24 }}>
      <h1 style={{ marginTop: 0 }}>Operational Actions</h1>
      <p style={{ color: "#555" }}>
        Manual action surface for submitting, approving, rejecting, resolving, and completing operational items.
      </p>

      <div style={panelStyle}>
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
        <ActionButtonsPanel
          title="Case Actions"
          items={[
            { label: "Submit Case", onClick: () => submitCaseAction.mutate(caseId), disabled: !caseId },
            { label: "Approve Case", onClick: () => approveCaseAction.mutate(caseId), disabled: !caseId },
            { label: "Reject Case", onClick: () => rejectCaseAction.mutate(caseId), disabled: !caseId },
          ]}
          message={
            submitCaseAction.data?.status ||
            approveCaseAction.data?.status ||
            rejectCaseAction.data?.status ||
            undefined
          }
        />

        <ActionButtonsPanel
          title="Resolution / Review Actions"
          items={[
            { label: "Resolve Contradiction", onClick: () => resolveContradictionAction.mutate(contradictionId), disabled: !contradictionId },
            { label: "Complete Review", onClick: () => completeReviewAction.mutate(reviewId), disabled: !reviewId },
          ]}
          message={
            resolveContradictionAction.data?.status ||
            completeReviewAction.data?.status ||
            undefined
          }
        />
      </div>
    </div>
  );
}

const panelStyle: React.CSSProperties = {
  border: "1px solid #ddd",
  borderRadius: 14,
  padding: 16,
  display: "grid",
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

$caseWorkbenchPath = Join-Path $web "pages\CaseWorkbenchPage.tsx"
if (Test-Path $caseWorkbenchPath) {
    $caseWorkbench = Get-Content $caseWorkbenchPath -Raw

    if ($caseWorkbench -notmatch 'ActionButtonsPanel') {
        $caseWorkbench = $caseWorkbench -replace 'import \{ ContradictionQueuePanel \} from "\.\./components/ContradictionQueuePanel";', @'
import { ContradictionQueuePanel } from "../components/ContradictionQueuePanel";
import { ActionButtonsPanel } from "../components/ActionButtonsPanel";
import {
  useApproveCaseAction,
  useRejectCaseAction,
  useSubmitCaseAction,
} from "../hooks/useActionMutations";
'@
    }

    if ($caseWorkbench -notmatch 'const submitCaseAction = useSubmitCaseAction\(\);') {
        $caseWorkbench = $caseWorkbench -replace 'const \{ caseQuery, linkedClaims, contradictionItems, claimsQuery, contradictionsQuery \} = useCaseWorkbench\(id\);', @'
const { caseQuery, linkedClaims, contradictionItems, claimsQuery, contradictionsQuery } = useCaseWorkbench(id);
  const submitCaseAction = useSubmitCaseAction();
  const approveCaseAction = useApproveCaseAction();
  const rejectCaseAction = useRejectCaseAction();
'@
    }

    if ($caseWorkbench -notmatch 'title="Case Actions"') {
        $caseWorkbench = $caseWorkbench -replace '\{contradictionsQuery\.isLoading && <p style=\{\{ marginTop: 12 \}\}>Refreshing contradictions\.\.\.<\/p>\}\s*<\/div>', @'
{contradictionsQuery.isLoading && <p style={{ marginTop: 12 }}>Refreshing contradictions...</p>}
      </div>

      <div style={{ marginTop: 20 }}>
        <ActionButtonsPanel
          title="Case Actions"
          items={[
            { label: "Submit Case", onClick: () => submitCaseAction.mutate(item.id) },
            { label: "Approve Case", onClick: () => approveCaseAction.mutate(item.id) },
            { label: "Reject Case", onClick: () => rejectCaseAction.mutate(item.id) },
          ]}
          message={
            submitCaseAction.data?.status ||
            approveCaseAction.data?.status ||
            rejectCaseAction.data?.status ||
            undefined
          }
        />
      </div>
'@
    }

    Write-File $caseWorkbenchPath $caseWorkbench
}

$resolutionPath = Join-Path $web "pages\ContradictionResolutionWorkspacePage.tsx"
if (Test-Path $resolutionPath) {
    $resolution = Get-Content $resolutionPath -Raw

    if ($resolution -notmatch 'useResolveContradictionAction') {
        $resolution = $resolution -replace 'import \{ ResolutionActionsPanel \} from "\.\./components/ResolutionActionsPanel";', @'
import { ResolutionActionsPanel } from "../components/ResolutionActionsPanel";
import { ActionButtonsPanel } from "../components/ActionButtonsPanel";
import { useResolveContradictionAction } from "../hooks/useActionMutations";
'@
    }

    if ($resolution -notmatch 'const resolveAction = useResolveContradictionAction\(\);') {
        $resolution = $resolution -replace 'const item = query\.data;', @'
const item = query.data;
  const resolveAction = useResolveContradictionAction();
'@
    }

    if ($resolution -notmatch 'title="Contradiction Resolution Action"') {
        $resolution = $resolution -replace '<div style=\{\{ marginTop: 20 \}\}>\s*<ResolutionActionsPanel contradictionId=\{item\.id\} caseId=\{item\.caseId \?\? undefined\} \/>\s*<\/div>', @'
<div style={{ marginTop: 20 }}>
        <ResolutionActionsPanel contradictionId={item.id} caseId={item.caseId ?? undefined} />
      </div>

      <div style={{ marginTop: 20 }}>
        <ActionButtonsPanel
          title="Contradiction Resolution Action"
          items={[
            { label: "Resolve Contradiction", onClick: () => resolveAction.mutate(item.id) },
          ]}
          message={resolveAction.data?.status}
        />
      </div>
'@
    }

    Write-File $resolutionPath $resolution
}

$main = Join-Path $web "main.tsx"
$content = Get-Content $main -Raw

if ($content -notmatch 'import \{ OperationalActionsPage \} from "\./pages/OperationalActionsPage";') {
    $content = $content -replace 'import \{ DashboardPage \} from "\./pages/DashboardPage";', @'
import { DashboardPage } from "./pages/DashboardPage";
import { OperationalActionsPage } from "./pages/OperationalActionsPage";
'@
}

$content = Ensure-RouteBlock -Content $content -AnchorRoute '{ path: "/dashboard", element: <DashboardPage /> },' -RouteBlock '{ path: "/operational-actions", element: <OperationalActionsPage /> },' -PresencePattern 'path: "/operational-actions"'
$content = Ensure-NavBlock -Content $content -Anchor '<Link to="/decision-intelligence">Decision Intelligence</Link>' -NavBlock '<Link to="/operational-actions">Operational Actions</Link>' -PresencePattern 'to="/operational-actions"'

Write-File $main $content

$diag = Join-Path $RootDir "_diagnostics\phase-7-3"
Ensure-Dir $diag
Write-File (Join-Path $diag "phase-7-3-summary.md") @'
# Phase 7.3 Summary

## Added
- frontend API for action endpoints
- mutation hooks for operational write actions
- reusable action buttons panel
- operational actions page
- case workbench case actions
- contradiction resolution action wiring

## Purpose
- connect UI to backend write endpoints
- move from passive surfaces to interactive operational controls
- prepare next phase for real persistence and service wiring
'@

Write-Host "Building..." -ForegroundColor Cyan
Build-All -RootDir $RootDir

Write-Host "Phase 7.3 DONE" -ForegroundColor Green
