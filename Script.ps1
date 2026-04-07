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
Git-Checkpoint -Message ("checkpoint before phase 7 final bundle - " + (Get-Date -Format "yyyy-MM-dd HH:mm:ss"))

Write-Host "Applying final Phase 7 bundle..." -ForegroundColor Cyan

$api = Join-Path $RootDir "apps\api\VeritasAtlas.Api"
$web = Join-Path $RootDir "apps\web\veritas-atlas-web\src"
$tools = Join-Path $RootDir "tools"
$diag = Join-Path $RootDir "_diagnostics\phase-7-final"
Ensure-Dir $diag

# =========================
# Backend diagnostics + smoke utilities
# =========================

Write-File (Join-Path $api "Controllers\WorkflowDiagnosticsController.cs") @'
using Microsoft.AspNetCore.Mvc;

namespace VeritasAtlas.Api.Controllers;

[ApiController]
[Route("api/v1/workflow-diagnostics")]
public class WorkflowDiagnosticsController : ControllerBase
{
    [HttpGet("summary")]
    public IActionResult GetSummary()
    {
        var summary = new
        {
            Actions = new[]
            {
                "SubmitCase",
                "ApproveCase",
                "RejectCase",
                "ResolveContradiction",
                "CompleteReview",
                "SendClaimToReview",
                "ReturnClaimForEdit",
                "EscalateContradiction",
                "ReopenReview",
                "PreparePublication",
                "PublishCase",
                "HoldCase"
            },
            Stage = "Phase7DepthTrack",
            Timestamp = DateTime.UtcNow
        };

        return Ok(summary);
    }

    [HttpGet("routes")]
    public IActionResult GetRouteRegistry()
    {
        var routes = new[]
        {
            "/api/v1/actions/cases/{caseId}/submit",
            "/api/v1/actions/cases/{caseId}/approve",
            "/api/v1/actions/cases/{caseId}/reject",
            "/api/v1/actions/contradictions/{id}/resolve",
            "/api/v1/actions/reviews/{id}/complete",
            "/api/v1/review-workflow/claims/{claimId}/send-to-review",
            "/api/v1/review-workflow/claims/{claimId}/return-for-edit",
            "/api/v1/review-workflow/contradictions/{id}/escalate",
            "/api/v1/review-workflow/reviews/{id}/reopen",
            "/api/v1/publication-workflow/cases/{caseId}/prepare",
            "/api/v1/publication-workflow/cases/{caseId}/publish",
            "/api/v1/publication-workflow/cases/{caseId}/hold"
        };

        return Ok(routes);
    }
}
'@

Write-File (Join-Path $tools "smoke\Run-VeritasAtlas-Workflow-Smoke.ps1") @'
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
'@

# =========================
# Frontend diagnostics + workflow UX
# =========================

Write-File (Join-Path $web "api\workflowDiagnostics.ts") @'
export type WorkflowDiagnosticsSummary = {
  actions: string[];
  stage: string;
  timestamp: string;
};

export async function getWorkflowDiagnosticsSummary(): Promise<WorkflowDiagnosticsSummary> {
  const response = await fetch("/api/v1/workflow-diagnostics/summary");

  if (!response.ok) {
    const text = await response.text();
    throw new Error(text || `HTTP ${response.status}`);
  }

  return response.json() as Promise<WorkflowDiagnosticsSummary>;
}

export async function getWorkflowDiagnosticsRoutes(): Promise<string[]> {
  const response = await fetch("/api/v1/workflow-diagnostics/routes");

  if (!response.ok) {
    const text = await response.text();
    throw new Error(text || `HTTP ${response.status}`);
  }

  return response.json() as Promise<string[]>;
}
'@

Write-File (Join-Path $web "hooks\useWorkflowDiagnostics.ts") @'
import { useQuery } from "@tanstack/react-query";
import {
  getWorkflowDiagnosticsRoutes,
  getWorkflowDiagnosticsSummary,
} from "../api/workflowDiagnostics";

export function useWorkflowDiagnosticsSummary() {
  return useQuery({
    queryKey: ["workflow-diagnostics-summary"],
    queryFn: () => getWorkflowDiagnosticsSummary(),
  });
}

export function useWorkflowDiagnosticsRoutes() {
  return useQuery({
    queryKey: ["workflow-diagnostics-routes"],
    queryFn: () => getWorkflowDiagnosticsRoutes(),
  });
}
'@

Write-File (Join-Path $web "hooks\useWorkflowActions.ts") @'
import { useMutation, useQueryClient } from "@tanstack/react-query";
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
  const queryClient = useQueryClient();

  return useMutation({
    mutationFn: (claimId: string) => sendClaimToReview(claimId),
    onSuccess: () => {
      queryClient.invalidateQueries({ queryKey: ["claims"] });
    },
  });
}

export function useReturnClaimForEditAction() {
  const queryClient = useQueryClient();

  return useMutation({
    mutationFn: (claimId: string) => returnClaimForEdit(claimId),
    onSuccess: () => {
      queryClient.invalidateQueries({ queryKey: ["claims"] });
    },
  });
}

export function useEscalateContradictionAction() {
  const queryClient = useQueryClient();

  return useMutation({
    mutationFn: (contradictionId: string) => escalateContradiction(contradictionId),
    onSuccess: () => {
      queryClient.invalidateQueries({ queryKey: ["contradictions"] });
    },
  });
}

export function useReopenReviewAction() {
  const queryClient = useQueryClient();

  return useMutation({
    mutationFn: (reviewId: string) => reopenReview(reviewId),
    onSuccess: () => {
      queryClient.invalidateQueries({ queryKey: ["reviews"] });
    },
  });
}

export function usePreparePublicationAction() {
  const queryClient = useQueryClient();

  return useMutation({
    mutationFn: (caseId: string) => preparePublication(caseId),
    onSuccess: () => {
      queryClient.invalidateQueries({ queryKey: ["case-explorer"] });
      queryClient.invalidateQueries({ queryKey: ["cases"] });
    },
  });
}

export function usePublishCaseAction() {
  const queryClient = useQueryClient();

  return useMutation({
    mutationFn: (caseId: string) => publishCase(caseId),
    onSuccess: () => {
      queryClient.invalidateQueries({ queryKey: ["case-explorer"] });
      queryClient.invalidateQueries({ queryKey: ["cases"] });
    },
  });
}

export function useHoldCaseAction() {
  const queryClient = useQueryClient();

  return useMutation({
    mutationFn: (caseId: string) => holdCase(caseId),
    onSuccess: () => {
      queryClient.invalidateQueries({ queryKey: ["case-explorer"] });
      queryClient.invalidateQueries({ queryKey: ["cases"] });
    },
  });
}
'@

Write-File (Join-Path $web "components\WorkflowDiagnosticsPanel.tsx") @'
export function WorkflowDiagnosticsPanel({
  stage,
  actions,
  routes,
}: {
  stage: string;
  actions: string[];
  routes: string[];
}) {
  return (
    <div style={panelStyle}>
      <h3 style={{ marginTop: 0 }}>Workflow Diagnostics</h3>
      <p><strong>Stage:</strong> {stage}</p>

      <div style={gridStyle}>
        <div>
          <h4>Actions</h4>
          <ul style={{ marginBottom: 0 }}>
            {actions.map((action) => (
              <li key={action}>{action}</li>
            ))}
          </ul>
        </div>

        <div>
          <h4>Routes</h4>
          <ul style={{ marginBottom: 0 }}>
            {routes.map((route) => (
              <li key={route}>{route}</li>
            ))}
          </ul>
        </div>
      </div>
    </div>
  );
}

const panelStyle: React.CSSProperties = {
  border: "1px solid #ddd",
  borderRadius: 14,
  padding: 16,
};

const gridStyle: React.CSSProperties = {
  display: "grid",
  gridTemplateColumns: "1fr 1fr",
  gap: 16,
};
'@

Write-File (Join-Path $web "pages\WorkflowDiagnosticsPage.tsx") @'
import { WorkflowDiagnosticsPanel } from "../components/WorkflowDiagnosticsPanel";
import {
  useWorkflowDiagnosticsRoutes,
  useWorkflowDiagnosticsSummary,
} from "../hooks/useWorkflowDiagnostics";

export function WorkflowDiagnosticsPage() {
  const summaryQuery = useWorkflowDiagnosticsSummary();
  const routesQuery = useWorkflowDiagnosticsRoutes();

  if (summaryQuery.isLoading || routesQuery.isLoading) {
    return <div style={{ fontFamily: "Arial, sans-serif", padding: 24 }}>Loading workflow diagnostics...</div>;
  }

  if (summaryQuery.isError || routesQuery.isError) {
    return <div style={{ fontFamily: "Arial, sans-serif", padding: 24, color: "crimson" }}>Failed to load workflow diagnostics.</div>;
  }

  if (!summaryQuery.data || !routesQuery.data) {
    return <div style={{ fontFamily: "Arial, sans-serif", padding: 24 }}>Workflow diagnostics unavailable.</div>;
  }

  return (
    <div style={{ fontFamily: "Arial, sans-serif", padding: 24 }}>
      <h1 style={{ marginTop: 0 }}>Workflow Diagnostics</h1>
      <p style={{ color: "#555" }}>
        Diagnostics surface for the write-action workflow layer added during Phase 7.
      </p>

      <WorkflowDiagnosticsPanel
        stage={summaryQuery.data.stage}
        actions={summaryQuery.data.actions}
        routes={routesQuery.data}
      />
    </div>
  );
}
'@

Write-File (Join-Path $web "pages\MutationPlaygroundPage.tsx") @'
import { useState } from "react";
import { ActionButtonsPanel } from "../components/ActionButtonsPanel";
import {
  useEscalateContradictionAction,
  useHoldCaseAction,
  usePreparePublicationAction,
  usePublishCaseAction,
  useReopenReviewAction,
  useReturnClaimForEditAction,
  useSendClaimToReviewAction,
} from "../hooks/useWorkflowActions";

export function MutationPlaygroundPage() {
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
      <h1 style={{ marginTop: 0 }}>Mutation Playground</h1>
      <p style={{ color: "#555" }}>
        Playground for exercising review and publication workflow mutations from the frontend.
      </p>

      <div style={gridStyle}>
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
        <ActionButtonsPanel
          title="Review Routing Mutations"
          items={[
            { label: "Send Claim To Review", onClick: () => sendClaim.mutate(claimId), disabled: !claimId },
            { label: "Return Claim For Edit", onClick: () => returnClaim.mutate(claimId), disabled: !claimId },
            { label: "Escalate Contradiction", onClick: () => escalate.mutate(contradictionId), disabled: !contradictionId },
            { label: "Reopen Review", onClick: () => reopen.mutate(reviewId), disabled: !reviewId },
          ]}
          message={sendClaim.data?.status || returnClaim.data?.status || escalate.data?.status || reopen.data?.status}
        />

        <ActionButtonsPanel
          title="Publication Mutations"
          items={[
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

const gridStyle: React.CSSProperties = {
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

Write-File (Join-Path $web "pages\Phase7CloseoutPage.tsx") @'
export function Phase7CloseoutPage() {
  const items = [
    "Smoke and workflow diagnostics added",
    "Write action endpoints added",
    "Review workflow endpoints added",
    "Publication workflow endpoints added",
    "Frontend action and workflow mutation wiring added",
    "Workflow console and mutation playground added",
    "Phase 7 ready to hand off into deeper persistence and business logic"
  ];

  return (
    <div style={{ fontFamily: "Arial, sans-serif", padding: 24 }}>
      <h1 style={{ marginTop: 0 }}>Phase 7 Closeout</h1>
      <p style={{ color: "#555" }}>
        Closeout summary for the entire Phase 7 depth track.
      </p>

      <div style={panelStyle}>
        <ul style={{ marginBottom: 0 }}>
          {items.map((item) => (
            <li key={item}>{item}</li>
          ))}
        </ul>
      </div>
    </div>
  );
}

const panelStyle: React.CSSProperties = {
  border: "1px solid #ddd",
  borderRadius: 14,
  padding: 16,
};
'@

$main = Join-Path $web "main.tsx"
$content = Get-Content $main -Raw

if ($content -notmatch 'import \{ WorkflowDiagnosticsPage \} from "\./pages/WorkflowDiagnosticsPage";') {
    $content = $content -replace 'import \{ DashboardPage \} from "\./pages/DashboardPage";', @'
import { DashboardPage } from "./pages/DashboardPage";
import { WorkflowDiagnosticsPage } from "./pages/WorkflowDiagnosticsPage";
import { MutationPlaygroundPage } from "./pages/MutationPlaygroundPage";
import { Phase7CloseoutPage } from "./pages/Phase7CloseoutPage";
'@
}

$content = Ensure-RouteBlock -Content $content -AnchorRoute '{ path: "/dashboard", element: <DashboardPage /> },' -RouteBlock '{ path: "/workflow-diagnostics", element: <WorkflowDiagnosticsPage /> },
  { path: "/mutation-playground", element: <MutationPlaygroundPage /> },
  { path: "/phase-7-closeout", element: <Phase7CloseoutPage /> },' -PresencePattern 'path: "/workflow-diagnostics"'

$content = Ensure-NavBlock -Content $content -Anchor '<Link to="/workflow-console">Workflow Console</Link>' -NavBlock '<Link to="/workflow-diagnostics">Workflow Diagnostics</Link>
          <Link to="/mutation-playground">Mutation Playground</Link>
          <Link to="/phase-7-closeout">Phase 7 Closeout</Link>' -PresencePattern 'to="/workflow-diagnostics"'

Write-File $main $content

Write-File (Join-Path $diag "phase-7-final-bundle-summary.md") @'
# Phase 7 Final Bundle Summary

## Included
- workflow diagnostics backend endpoints
- workflow smoke runner
- workflow diagnostics frontend API and hooks
- query invalidation for workflow mutations
- workflow diagnostics page
- mutation playground page
- phase 7 closeout page

## Outcome
Phase 7 now includes:
- backend smoke tooling
- write actions foundation
- review workflow action layer
- publication workflow action layer
- frontend mutation wiring
- diagnostics and closeout surfaces

## Next recommended phase
- connect controllers to real application services
- persist workflow transitions
- validate business rules
- add seeded integration tests
- add auth and role-based control over actions
'@

Write-Host "Building..." -ForegroundColor Cyan
Build-All -RootDir $RootDir

Write-Host "Final Phase 7 bundle DONE" -ForegroundColor Green
