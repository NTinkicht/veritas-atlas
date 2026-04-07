param(
    [Parameter(Mandatory = $true)]
    [string]$RootDir
)

$ErrorActionPreference = "Stop"

function Ensure-Dir {
    param([Parameter(Mandatory = $true)][string]$Path)
    if ([string]::IsNullOrWhiteSpace($Path)) {
        throw "Ensure-Dir received an empty path."
    }
    if (-not (Test-Path $Path)) {
        New-Item -ItemType Directory -Path $Path -Force | Out-Null
    }
}

function Write-File {
    param(
        [Parameter(Mandatory = $true)][string]$Path,
        [Parameter(Mandatory = $true)][string]$Content
    )
    $parent = Split-Path -Parent $Path
    Ensure-Dir -Path $parent
    $enc = New-Object System.Text.UTF8Encoding($false)
    [System.IO.File]::WriteAllText($Path, $Content, $enc)
    Write-Host "Wrote: $Path"
}

function Git-Checkpoint {
    param([Parameter(Mandatory = $true)][string]$Message)

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
    param([Parameter(Mandatory = $true)][string]$RootDir)

    Push-Location $RootDir
    try {
        dotnet build
        if ($LASTEXITCODE -ne 0) { throw "Backend failed" }
    }
    finally {
        Pop-Location
    }

    $webDir = Join-Path $RootDir "apps\web\veritas-atlas-web"
    if (Test-Path $webDir) {
        Push-Location $webDir
        try {
            npm run build
            if ($LASTEXITCODE -ne 0) { throw "Frontend failed" }
        }
        finally {
            Pop-Location
        }
    }
}

function Ensure-ImportLine {
    param(
        [Parameter(Mandatory = $true)][string]$Content,
        [Parameter(Mandatory = $true)][string]$Anchor,
        [Parameter(Mandatory = $true)][string]$ImportLine
    )

    if ($Content -match [regex]::Escape($ImportLine)) {
        return $Content
    }

    return $Content -replace [regex]::Escape($Anchor), ($Anchor + [Environment]::NewLine + $ImportLine)
}

function Ensure-RouteBlock {
    param(
        [Parameter(Mandatory = $true)][string]$Content,
        [Parameter(Mandatory = $true)][string]$AnchorRoute,
        [Parameter(Mandatory = $true)][string]$RouteBlock,
        [Parameter(Mandatory = $true)][string]$PresencePattern
    )

    if ($Content -match $PresencePattern) {
        return $Content
    }

    return $Content -replace [regex]::Escape($AnchorRoute), ($AnchorRoute + [Environment]::NewLine + $RouteBlock)
}

function Ensure-NavBlock {
    param(
        [Parameter(Mandatory = $true)][string]$Content,
        [Parameter(Mandatory = $true)][string]$Anchor,
        [Parameter(Mandatory = $true)][string]$NavBlock,
        [Parameter(Mandatory = $true)][string]$PresencePattern
    )

    if ($Content -match $PresencePattern) {
        return $Content
    }

    return $Content -replace [regex]::Escape($Anchor), ($Anchor + [Environment]::NewLine + $NavBlock)
}

Write-Host "Checkpointing current code with git..." -ForegroundColor Cyan
Git-Checkpoint -Message ("checkpoint before phase 8.1-8.5 bundle - " + (Get-Date -Format "yyyy-MM-dd HH:mm:ss"))

$api = Join-Path $RootDir "apps\api"
$infra = Join-Path $api "VeritasAtlas.Infrastructure"
$apiProj = Join-Path $api "VeritasAtlas.Api"
$web = Join-Path $RootDir "apps\web\veritas-atlas-web\src"
$tools = Join-Path $RootDir "tools"
$diag = Join-Path $RootDir "_diagnostics\phase-8-1-to-8-5"
Ensure-Dir -Path $diag

Write-Host "Applying bundled phases 8.1 to 8.5..." -ForegroundColor Cyan

# =========================
# 8.1 Real workflow persistence pass
# =========================

Write-File -Path (Join-Path $infra "Services\WorkflowTransitionService.cs") -Content @'
using Microsoft.EntityFrameworkCore;
using VeritasAtlas.Domain.Entities;
using VeritasAtlas.Domain.Enums;
using VeritasAtlas.Infrastructure.Persistence;

namespace VeritasAtlas.Infrastructure.Services;

public sealed class WorkflowTransitionService
{
    private readonly VeritasAtlasDbContext _dbContext;

    public WorkflowTransitionService(VeritasAtlasDbContext dbContext)
    {
        _dbContext = dbContext;
    }

    public async Task<(Guid Id, string Status)> SubmitCaseAsync(Guid caseId, CancellationToken cancellationToken = default)
    {
        var entity = await GetCaseAsync(caseId, cancellationToken);
        entity.Status = ParseCaseStatus("InReview", entity.Status);
        Touch(entity);
        await _dbContext.SaveChangesAsync(cancellationToken);
        return (entity.Id, entity.Status.ToString());
    }

    public async Task<(Guid Id, string Status)> ApproveCaseAsync(Guid caseId, CancellationToken cancellationToken = default)
    {
        var entity = await GetCaseAsync(caseId, cancellationToken);
        entity.Status = ParseCaseStatus("Approved", entity.Status);
        Touch(entity);
        await _dbContext.SaveChangesAsync(cancellationToken);
        return (entity.Id, entity.Status.ToString());
    }

    public async Task<(Guid Id, string Status)> RejectCaseAsync(Guid caseId, CancellationToken cancellationToken = default)
    {
        var entity = await GetCaseAsync(caseId, cancellationToken);
        entity.Status = ParseCaseStatus("Rejected", entity.Status);
        Touch(entity);
        await _dbContext.SaveChangesAsync(cancellationToken);
        return (entity.Id, entity.Status.ToString());
    }

    public async Task<(Guid Id, string Status)> PreparePublicationAsync(Guid caseId, CancellationToken cancellationToken = default)
    {
        var entity = await GetCaseAsync(caseId, cancellationToken);
        entity.Status = ParseCaseStatus("ReadyForPublication", entity.Status);
        Touch(entity);
        await _dbContext.SaveChangesAsync(cancellationToken);
        return (entity.Id, entity.Status.ToString());
    }

    public async Task<(Guid Id, string Status)> PublishCaseAsync(Guid caseId, CancellationToken cancellationToken = default)
    {
        var entity = await GetCaseAsync(caseId, cancellationToken);
        entity.Status = ParseCaseStatus("Published", entity.Status);
        Touch(entity);
        await _dbContext.SaveChangesAsync(cancellationToken);
        return (entity.Id, entity.Status.ToString());
    }

    public async Task<(Guid Id, string Status)> HoldCaseAsync(Guid caseId, CancellationToken cancellationToken = default)
    {
        var entity = await GetCaseAsync(caseId, cancellationToken);
        entity.Status = ParseCaseStatus("OnHold", entity.Status);
        Touch(entity);
        await _dbContext.SaveChangesAsync(cancellationToken);
        return (entity.Id, entity.Status.ToString());
    }

    public async Task<(Guid Id, string Status)> ResolveContradictionAsync(Guid contradictionId, CancellationToken cancellationToken = default)
    {
        var entity = await GetContradictionAsync(contradictionId, cancellationToken);
        entity.Status = ContradictionStatus.Resolved;
        Touch(entity);
        await _dbContext.SaveChangesAsync(cancellationToken);
        return (entity.Id, entity.Status.ToString());
    }

    public async Task<(Guid Id, string Status)> EscalateContradictionAsync(Guid contradictionId, CancellationToken cancellationToken = default)
    {
        var entity = await GetContradictionAsync(contradictionId, cancellationToken);
        entity.Status = ParseContradictionStatus("UnderReview", entity.Status);
        Touch(entity);
        await _dbContext.SaveChangesAsync(cancellationToken);
        return (entity.Id, entity.Status.ToString());
    }

    public async Task<(Guid Id, string Status)> SendClaimToReviewAsync(Guid claimId, CancellationToken cancellationToken = default)
    {
        var entity = await GetClaimAsync(claimId, cancellationToken);
        entity.Status = ParseClaimStatus("InReview", entity.Status);
        Touch(entity);
        await _dbContext.SaveChangesAsync(cancellationToken);
        return (entity.Id, entity.Status.ToString());
    }

    public async Task<(Guid Id, string Status)> ReturnClaimForEditAsync(Guid claimId, CancellationToken cancellationToken = default)
    {
        var entity = await GetClaimAsync(claimId, cancellationToken);
        entity.Status = ParseClaimStatus("Draft", entity.Status);
        Touch(entity);
        await _dbContext.SaveChangesAsync(cancellationToken);
        return (entity.Id, entity.Status.ToString());
    }

    public async Task<(Guid Id, string Status)> CompleteReviewAsync(Guid reviewId, CancellationToken cancellationToken = default)
    {
        var entity = await GetReviewAsync(reviewId, cancellationToken);
        entity.Status = ParseReviewStatus("Completed", entity.Status);
        Touch(entity);
        await _dbContext.SaveChangesAsync(cancellationToken);
        return (entity.Id, entity.Status.ToString());
    }

    public async Task<(Guid Id, string Status)> ReopenReviewAsync(Guid reviewId, CancellationToken cancellationToken = default)
    {
        var entity = await GetReviewAsync(reviewId, cancellationToken);
        entity.Status = ParseReviewStatus("Open", entity.Status);
        Touch(entity);
        await _dbContext.SaveChangesAsync(cancellationToken);
        return (entity.Id, entity.Status.ToString());
    }

    public async Task<(Guid CaseId, Guid ClaimAId, Guid ClaimBId, Guid ContradictionId, string CaseStatus, string ContradictionStatus)> SeedLifecycleAsync(CancellationToken cancellationToken = default)
    {
        var caseStatus = ParseCaseStatus("Draft", default);
        var claimStatus = ParseClaimStatus("Draft", default);

        var @case = new Case
        {
            Title = "Phase 8 Seed Case",
            Status = caseStatus
        };
        Touch(@case);

        _dbContext.Cases.Add(@case);
        await _dbContext.SaveChangesAsync(cancellationToken);

        var claimA = new Claim
        {
            CaseId = @case.Id,
            Topic = "Seed Claim A",
            NormalizedText = "Seed claim A normalized text",
            Status = claimStatus
        };
        Touch(claimA);

        var claimB = new Claim
        {
            CaseId = @case.Id,
            Topic = "Seed Claim B",
            NormalizedText = "Seed claim B normalized text",
            Status = claimStatus
        };
        Touch(claimB);

        _dbContext.Claims.Add(claimA);
        _dbContext.Claims.Add(claimB);
        await _dbContext.SaveChangesAsync(cancellationToken);

        var contradiction = new Contradiction
        {
            CaseId = @case.Id,
            LeftClaimId = claimA.Id,
            RightClaimId = claimB.Id,
            Type = ContradictionType.Direct,
            Severity = ContradictionSeverity.Medium,
            Status = ContradictionStatus.Draft,
            Summary = "Seed contradiction"
        };
        Touch(contradiction);

        _dbContext.Contradictions.Add(contradiction);
        await _dbContext.SaveChangesAsync(cancellationToken);

        return (@case.Id, claimA.Id, claimB.Id, contradiction.Id, @case.Status.ToString(), contradiction.Status.ToString());
    }

    private async Task<Case> GetCaseAsync(Guid caseId, CancellationToken cancellationToken)
    {
        return await _dbContext.Cases.FirstOrDefaultAsync(x => x.Id == caseId, cancellationToken)
            ?? throw new InvalidOperationException($"Case '{caseId}' was not found.");
    }

    private async Task<Claim> GetClaimAsync(Guid claimId, CancellationToken cancellationToken)
    {
        return await _dbContext.Claims.FirstOrDefaultAsync(x => x.Id == claimId, cancellationToken)
            ?? throw new InvalidOperationException($"Claim '{claimId}' was not found.");
    }

    private async Task<Contradiction> GetContradictionAsync(Guid contradictionId, CancellationToken cancellationToken)
    {
        return await _dbContext.Contradictions.FirstOrDefaultAsync(x => x.Id == contradictionId, cancellationToken)
            ?? throw new InvalidOperationException($"Contradiction '{contradictionId}' was not found.");
    }

    private async Task<Review> GetReviewAsync(Guid reviewId, CancellationToken cancellationToken)
    {
        return await _dbContext.Reviews.FirstOrDefaultAsync(x => x.Id == reviewId, cancellationToken)
            ?? throw new InvalidOperationException($"Review '{reviewId}' was not found.");
    }

    private static void Touch(object entity)
    {
        var updatedAt = entity.GetType().GetProperty("UpdatedAtUtc");
        if (updatedAt is not null && updatedAt.CanWrite)
        {
            updatedAt.SetValue(entity, DateTime.UtcNow);
        }
    }

    private static TEnum ParseEnum<TEnum>(string desired, TEnum fallback) where TEnum : struct, Enum
    {
        if (Enum.TryParse<TEnum>(desired, true, out var parsed))
        {
            return parsed;
        }

        return fallback;
    }

    private static dynamic ParseCaseStatus(string desired, dynamic fallback) => ParseEnum(desired, fallback);
    private static dynamic ParseClaimStatus(string desired, dynamic fallback) => ParseEnum(desired, fallback);
    private static dynamic ParseReviewStatus(string desired, dynamic fallback) => ParseEnum(desired, fallback);
    private static ContradictionStatus ParseContradictionStatus(string desired, ContradictionStatus fallback) => ParseEnum(desired, fallback);
}
'@

# =========================
# 8.2 Query refresh correctness
# =========================
Write-File -Path (Join-Path $web "hooks\useActionMutations.ts") -Content @'
import { useMutation, useQueryClient } from "@tanstack/react-query";
import {
  approveCase,
  completeReview,
  rejectCase,
  resolveContradiction,
  submitCase,
} from "../api/actions";

function invalidateCaseQueries(queryClient: ReturnType<typeof useQueryClient>) {
  queryClient.invalidateQueries({ queryKey: ["case-explorer"] });
  queryClient.invalidateQueries({ queryKey: ["cases"] });
}

export function useSubmitCaseAction() {
  const queryClient = useQueryClient();

  return useMutation({
    mutationFn: (caseId: string) => submitCase(caseId),
    onSuccess: () => {
      invalidateCaseQueries(queryClient);
    },
  });
}

export function useApproveCaseAction() {
  const queryClient = useQueryClient();

  return useMutation({
    mutationFn: (caseId: string) => approveCase(caseId),
    onSuccess: () => {
      invalidateCaseQueries(queryClient);
    },
  });
}

export function useRejectCaseAction() {
  const queryClient = useQueryClient();

  return useMutation({
    mutationFn: (caseId: string) => rejectCase(caseId),
    onSuccess: () => {
      invalidateCaseQueries(queryClient);
    },
  });
}

export function useResolveContradictionAction() {
  const queryClient = useQueryClient();

  return useMutation({
    mutationFn: (contradictionId: string) => resolveContradiction(contradictionId),
    onSuccess: () => {
      queryClient.invalidateQueries({ queryKey: ["contradictions"] });
      queryClient.invalidateQueries({ queryKey: ["case-explorer"] });
      queryClient.invalidateQueries({ queryKey: ["cases"] });
    },
  });
}

export function useCompleteReviewAction() {
  const queryClient = useQueryClient();

  return useMutation({
    mutationFn: (reviewId: string) => completeReview(reviewId),
    onSuccess: () => {
      queryClient.invalidateQueries({ queryKey: ["reviews"] });
      queryClient.invalidateQueries({ queryKey: ["case-explorer"] });
      queryClient.invalidateQueries({ queryKey: ["cases"] });
    },
  });
}
'@

# =========================
# 8.3 Business rule validation
# =========================
Write-File -Path (Join-Path $apiProj "Controllers\WorkflowValidationController.cs") -Content @'
using Microsoft.AspNetCore.Mvc;

namespace VeritasAtlas.Api.Controllers;

[ApiController]
[Route("api/v1/workflow-validation")]
public class WorkflowValidationController : ControllerBase
{
    [HttpGet("rules")]
    public IActionResult GetRules()
    {
        var rules = new[]
        {
            "Case must exist before submission",
            "Case must exist before approval or rejection",
            "Contradiction must exist before resolution or escalation",
            "Review must exist before completion or reopen",
            "Claim must exist before review routing",
            "Publication preparation requires an existing case"
        };

        return Ok(rules);
    }
}
'@

# =========================
# 8.4 Seeded end-to-end scenario
# =========================
Write-File -Path (Join-Path $tools "smoke\Run-VeritasAtlas-Seeded-Lifecycle.ps1") -Content @'
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
'@

Write-File -Path (Join-Path $web "pages\SeededLifecycleRunnerPage.tsx") -Content @'
import { useState } from "react";
import { useWorkflowSeed } from "../hooks/useWorkflowSeed";
import {
  usePreparePublicationAction,
  usePublishCaseAction,
} from "../hooks/useWorkflowActions";
import { useResolveContradictionAction } from "../hooks/useActionMutations";

export function SeededLifecycleRunnerPage() {
  const [caseId, setCaseId] = useState("");
  const [contradictionId, setContradictionId] = useState("");

  const seedMutation = useWorkflowSeed();
  const prepareMutation = usePreparePublicationAction();
  const publishMutation = usePublishCaseAction();
  const resolveMutation = useResolveContradictionAction();

  const handleSeed = () => {
    seedMutation.mutate(undefined, {
      onSuccess: (data) => {
        setCaseId(data.caseId);
        setContradictionId(data.contradictionId);
      },
    });
  };

  return (
    <div style={{ fontFamily: "Arial, sans-serif", padding: 24 }}>
      <h1 style={{ marginTop: 0 }}>Seeded Lifecycle Runner</h1>
      <p style={{ color: "#555" }}>
        Seed and execute a minimal lifecycle directly from the frontend.
      </p>

      <div style={panelStyle}>
        <button onClick={handleSeed} style={buttonStyle}>Seed Lifecycle</button>
        <button onClick={() => prepareMutation.mutate(caseId)} disabled={!caseId} style={buttonStyle}>Prepare Publication</button>
        <button onClick={() => resolveMutation.mutate(contradictionId)} disabled={!contradictionId} style={buttonStyle}>Resolve Contradiction</button>
        <button onClick={() => publishMutation.mutate(caseId)} disabled={!caseId} style={buttonStyle}>Publish Case</button>
      </div>

      <div style={{ ...panelStyle, marginTop: 16 }}>
        <p>Case Id: {caseId || "N/A"}</p>
        <p>Contradiction Id: {contradictionId || "N/A"}</p>
        <p>{seedMutation.data ? `Seeded case ${seedMutation.data.caseId}` : ""}</p>
        <p>{prepareMutation.data ? `Prepare status: ${prepareMutation.data.status}` : ""}</p>
        <p>{resolveMutation.data ? `Resolve status: ${resolveMutation.data.status}` : ""}</p>
        <p>{publishMutation.data ? `Publish status: ${publishMutation.data.status}` : ""}</p>
      </div>
    </div>
  );
}

const panelStyle: React.CSSProperties = {
  border: "1px solid #ddd",
  borderRadius: 14,
  padding: 16,
  display: "flex",
  gap: 12,
  flexWrap: "wrap",
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
# 8.5 Integration testing / reporting surfaces
# =========================
Write-File -Path (Join-Path $web "api\workflowValidation.ts") -Content @'
export async function getWorkflowRules(): Promise<string[]> {
  const response = await fetch("/api/v1/workflow-validation/rules");

  if (!response.ok) {
    const text = await response.text();
    throw new Error(text || `HTTP ${response.status}`);
  }

  return response.json() as Promise<string[]>;
}
'@

Write-File -Path (Join-Path $web "hooks\useWorkflowValidation.ts") -Content @'
import { useQuery } from "@tanstack/react-query";
import { getWorkflowRules } from "../api/workflowValidation";

export function useWorkflowValidation() {
  return useQuery({
    queryKey: ["workflow-validation-rules"],
    queryFn: () => getWorkflowRules(),
  });
}
'@

Write-File -Path (Join-Path $web "components\IntegrationChecklistPanel.tsx") -Content @'
export function IntegrationChecklistPanel({
  items,
}: {
  items: string[];
}) {
  return (
    <div style={panelStyle}>
      <h3 style={{ marginTop: 0 }}>Integration Checklist</h3>
      <ul style={{ marginBottom: 0 }}>
        {items.map((item) => (
          <li key={item}>{item}</li>
        ))}
      </ul>
    </div>
  );
}

const panelStyle: React.CSSProperties = {
  border: "1px solid #ddd",
  borderRadius: 14,
  padding: 16,
};
'@

Write-File -Path (Join-Path $web "pages\WorkflowValidationPage.tsx") -Content @'
import { IntegrationChecklistPanel } from "../components/IntegrationChecklistPanel";
import { useWorkflowValidation } from "../hooks/useWorkflowValidation";

export function WorkflowValidationPage() {
  const query = useWorkflowValidation();

  if (query.isLoading) {
    return <div style={{ fontFamily: "Arial, sans-serif", padding: 24 }}>Loading workflow validation...</div>;
  }

  if (query.isError) {
    return <div style={{ fontFamily: "Arial, sans-serif", padding: 24, color: "crimson" }}>Failed to load workflow validation rules.</div>;
  }

  return (
    <div style={{ fontFamily: "Arial, sans-serif", padding: 24 }}>
      <h1 style={{ marginTop: 0 }}>Workflow Validation</h1>
      <p style={{ color: "#555" }}>
        Current workflow rules and validation assumptions.
      </p>
      <IntegrationChecklistPanel items={query.data ?? []} />
    </div>
  );
}
'@

Write-File -Path (Join-Path $web "pages\IntegrationTestCenterPage.tsx") -Content @'
import { IntegrationChecklistPanel } from "../components/IntegrationChecklistPanel";

export function IntegrationTestCenterPage() {
  const items = [
    "Seed lifecycle scenario",
    "Submit seeded case",
    "Prepare publication",
    "Resolve contradiction",
    "Publish case",
    "Verify refreshed lists and detail surfaces",
    "Capture diagnostics report"
  ];

  return (
    <div style={{ fontFamily: "Arial, sans-serif", padding: 24 }}>
      <h1 style={{ marginTop: 0 }}>Integration Test Center</h1>
      <p style={{ color: "#555" }}>
        Frontend reference center for the seeded workflow integration path.
      </p>
      <IntegrationChecklistPanel items={items} />
    </div>
  );
}
'@

$main = Join-Path $web "main.tsx"
$mainContent = Get-Content $main -Raw

$mainContent = Ensure-ImportLine -Content $mainContent -Anchor 'import { DashboardPage } from "./pages/DashboardPage";' -ImportLine 'import { SeededLifecycleRunnerPage } from "./pages/SeededLifecycleRunnerPage";'
$mainContent = Ensure-ImportLine -Content $mainContent -Anchor 'import { SeededLifecycleRunnerPage } from "./pages/SeededLifecycleRunnerPage";' -ImportLine 'import { WorkflowValidationPage } from "./pages/WorkflowValidationPage";'
$mainContent = Ensure-ImportLine -Content $mainContent -Anchor 'import { WorkflowValidationPage } from "./pages/WorkflowValidationPage";' -ImportLine 'import { IntegrationTestCenterPage } from "./pages/IntegrationTestCenterPage";'

$mainContent = Ensure-RouteBlock -Content $mainContent -AnchorRoute '{ path: "/dashboard", element: <DashboardPage /> },' -RouteBlock '{ path: "/seeded-lifecycle-runner", element: <SeededLifecycleRunnerPage /> },
  { path: "/workflow-validation", element: <WorkflowValidationPage /> },
  { path: "/integration-test-center", element: <IntegrationTestCenterPage /> },' -PresencePattern 'path: "/seeded-lifecycle-runner"'

$mainContent = Ensure-NavBlock -Content $mainContent -Anchor '<Link to="/workflow-diagnostics">Workflow Diagnostics</Link>' -NavBlock '<Link to="/seeded-lifecycle-runner">Seeded Lifecycle Runner</Link>
          <Link to="/workflow-validation">Workflow Validation</Link>
          <Link to="/integration-test-center">Integration Test Center</Link>' -PresencePattern 'to="/seeded-lifecycle-runner"'

Write-File -Path $main -Content $mainContent

Write-File -Path (Join-Path $diag "phase-8-1-to-8-5-summary.md") -Content @'
# Phase 8.1 to 8.5 Bundle Summary

## 8.1
Persisted workflow transitions through WorkflowTransitionService

## 8.2
Added broader query invalidation for mutations

## 8.3
Added workflow validation endpoint and rules surface

## 8.4
Added seeded lifecycle runner script and frontend runner page

## 8.5
Added integration test center and reporting scaffolds

## Purpose
Move the system from UI and stub actions toward persisted transitions, seeded verification, and integration-oriented workflow testing.
'@

Write-Host "Building..." -ForegroundColor Cyan
Build-All -RootDir $RootDir

Write-Host "Bundled phases 8.1 to 8.5 DONE" -ForegroundColor Green
