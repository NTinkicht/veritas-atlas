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
    if ($Content -match [regex]::Escape($ImportLine)) { return $Content }
    return $Content -replace [regex]::Escape($Anchor), ($Anchor + [Environment]::NewLine + $ImportLine)
}

function Ensure-RouteBlock {
    param(
        [Parameter(Mandatory = $true)][string]$Content,
        [Parameter(Mandatory = $true)][string]$AnchorRoute,
        [Parameter(Mandatory = $true)][string]$RouteBlock,
        [Parameter(Mandatory = $true)][string]$PresencePattern
    )
    if ($Content -match $PresencePattern) { return $Content }
    return $Content -replace [regex]::Escape($AnchorRoute), ($AnchorRoute + [Environment]::NewLine + $RouteBlock)
}

function Ensure-NavBlock {
    param(
        [Parameter(Mandatory = $true)][string]$Content,
        [Parameter(Mandatory = $true)][string]$Anchor,
        [Parameter(Mandatory = $true)][string]$NavBlock,
        [Parameter(Mandatory = $true)][string]$PresencePattern
    )
    if ($Content -match $PresencePattern) { return $Content }
    return $Content -replace [regex]::Escape($Anchor), ($Anchor + [Environment]::NewLine + $NavBlock)
}

function Invoke-Phase12Tests {
    param([Parameter(Mandatory = $true)][string]$RootDir)
    $runner = Join-Path $RootDir "tools\tests\Run-Phase12-Verification.ps1"
    if (-not (Test-Path $runner)) { throw "Phase 12 verification runner not found: $runner" }

    Push-Location $RootDir
    try {
        & powershell -ExecutionPolicy Bypass -File $runner -RootDir $RootDir
        if ($LASTEXITCODE -ne 0) { throw "Phase 12 verification runner failed." }
    }
    finally {
        Pop-Location
    }
}

Write-Host "Checkpointing current code with git..." -ForegroundColor Cyan
Git-Checkpoint -Message ("checkpoint before phase 12 bundle - " + (Get-Date -Format "yyyy-MM-dd HH:mm:ss"))

Write-Host "Applying Phase 12 bundle - data persistence, reset tooling, diagnostics, and integration verification..." -ForegroundColor Cyan

$api = Join-Path $RootDir "apps\api"
$infra = Join-Path $api "VeritasAtlas.Infrastructure"
$apiProj = Join-Path $api "VeritasAtlas.Api"
$web = Join-Path $RootDir "apps\web\veritas-atlas-web\src"
$tools = Join-Path $RootDir "tools"
$diag = Join-Path $RootDir "_diagnostics\phase-12"
Ensure-Dir -Path $diag

# =========================
# BACKEND: scenario persistence files
# =========================

Write-File -Path (Join-Path $infra "Services\ScenarioPersistenceService.cs") -Content @'
using System.Text.Json;

namespace VeritasAtlas.Infrastructure.Services;

public sealed class ScenarioPersistenceService
{
    private readonly string _rootPath;
    private readonly string _snapshotPath;
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web) { WriteIndented = true };

    public ScenarioPersistenceService()
    {
        _rootPath = AppContext.BaseDirectory;
        _snapshotPath = Path.Combine(_rootPath, "scenario-snapshot.json");
    }

    public async Task SaveSnapshotAsync(ScenarioSnapshot snapshot, CancellationToken cancellationToken = default)
    {
        await using var stream = File.Create(_snapshotPath);
        await JsonSerializer.SerializeAsync(stream, snapshot, JsonOptions, cancellationToken);
    }

    public async Task<ScenarioSnapshot?> LoadSnapshotAsync(CancellationToken cancellationToken = default)
    {
        if (!File.Exists(_snapshotPath))
        {
            return null;
        }

        await using var stream = File.OpenRead(_snapshotPath);
        return await JsonSerializer.DeserializeAsync<ScenarioSnapshot>(stream, JsonOptions, cancellationToken);
    }

    public Task<bool> HasSnapshotAsync()
    {
        return Task.FromResult(File.Exists(_snapshotPath));
    }

    public Task ClearSnapshotAsync()
    {
        if (File.Exists(_snapshotPath))
        {
            File.Delete(_snapshotPath);
        }

        return Task.CompletedTask;
    }
}

public sealed record ScenarioSnapshot(
    Guid CaseId,
    Guid ClaimAId,
    Guid ClaimBId,
    Guid ContradictionId,
    DateTime CreatedAtUtc,
    string CaseStatus,
    string ContradictionStatus);
'@

Write-File -Path (Join-Path $apiProj "Contracts\Persistence\PersistenceContracts.cs") -Content @'
namespace VeritasAtlas.Api.Contracts.Persistence;

public sealed record ScenarioSnapshotResponse(
    bool Exists,
    Guid? CaseId,
    Guid? ClaimAId,
    Guid? ClaimBId,
    Guid? ContradictionId,
    DateTime? CreatedAtUtc,
    string? CaseStatus,
    string? ContradictionStatus,
    DateTime TimestampUtc);

public sealed record ResetResponse(
    bool Success,
    string Message,
    DateTime TimestampUtc);
'@

Write-File -Path (Join-Path $apiProj "Controllers\PersistenceController.cs") -Content @'
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using VeritasAtlas.Api.Contracts.Persistence;
using VeritasAtlas.Infrastructure.Services;

namespace VeritasAtlas.Api.Controllers;

[ApiController]
[Authorize]
[Route("api/v1/persistence")]
public class PersistenceController : ControllerBase
{
    private readonly ScenarioPersistenceService _scenarioPersistenceService;
    private readonly WorkflowAuditStore _workflowAuditStore;

    public PersistenceController(
        ScenarioPersistenceService scenarioPersistenceService,
        WorkflowAuditStore workflowAuditStore)
    {
        _scenarioPersistenceService = scenarioPersistenceService;
        _workflowAuditStore = workflowAuditStore;
    }

    [HttpGet("snapshot")]
    public async Task<ActionResult<ScenarioSnapshotResponse>> GetSnapshot(CancellationToken cancellationToken)
    {
        var snapshot = await _scenarioPersistenceService.LoadSnapshotAsync(cancellationToken);

        if (snapshot is null)
        {
            return Ok(new ScenarioSnapshotResponse(
                false, null, null, null, null, null, null, null, DateTime.UtcNow));
        }

        return Ok(new ScenarioSnapshotResponse(
            true,
            snapshot.CaseId,
            snapshot.ClaimAId,
            snapshot.ClaimBId,
            snapshot.ContradictionId,
            snapshot.CreatedAtUtc,
            snapshot.CaseStatus,
            snapshot.ContradictionStatus,
            DateTime.UtcNow));
    }

    [HttpPost("reset")]
    public async Task<ActionResult<ResetResponse>> Reset(CancellationToken cancellationToken)
    {
        await _scenarioPersistenceService.ClearSnapshotAsync();
        await _workflowAuditStore.ClearAsync(cancellationToken);

        return Ok(new ResetResponse(true, "Scenario snapshot and workflow audit have been cleared.", DateTime.UtcNow));
    }
}
'@

# =========================
# BACKEND: enrich workflow transition service with snapshot persistence
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
    private readonly ScenarioPersistenceService _scenarioPersistenceService;

    public WorkflowTransitionService(
        VeritasAtlasDbContext dbContext,
        ScenarioPersistenceService scenarioPersistenceService)
    {
        _dbContext = dbContext;
        _scenarioPersistenceService = scenarioPersistenceService;
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
        await UpdateSnapshotAsync(cancellationToken);
        return (entity.Id, entity.Status.ToString());
    }

    public async Task<(Guid Id, string Status)> EscalateContradictionAsync(Guid contradictionId, CancellationToken cancellationToken = default)
    {
        var entity = await GetContradictionAsync(contradictionId, cancellationToken);
        entity.Status = ParseContradictionStatus("UnderReview", entity.Status);
        Touch(entity);
        await _dbContext.SaveChangesAsync(cancellationToken);
        await UpdateSnapshotAsync(cancellationToken);
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
            Title = "Phase 12 Seed Case",
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

        await _scenarioPersistenceService.SaveSnapshotAsync(
            new ScenarioSnapshot(
                @case.Id,
                claimA.Id,
                claimB.Id,
                contradiction.Id,
                DateTime.UtcNow,
                @case.Status.ToString(),
                contradiction.Status.ToString()),
            cancellationToken);

        return (@case.Id, claimA.Id, claimB.Id, contradiction.Id, @case.Status.ToString(), contradiction.Status.ToString());
    }

    public async Task UpdateSnapshotAsync(CancellationToken cancellationToken = default)
    {
        var snapshot = await _scenarioPersistenceService.LoadSnapshotAsync(cancellationToken);
        if (snapshot is null)
        {
            return;
        }

        var @case = await _dbContext.Cases.FirstOrDefaultAsync(x => x.Id == snapshot.CaseId, cancellationToken);
        var contradiction = await _dbContext.Contradictions.FirstOrDefaultAsync(x => x.Id == snapshot.ContradictionId, cancellationToken);

        if (@case is null || contradiction is null)
        {
            return;
        }

        await _scenarioPersistenceService.SaveSnapshotAsync(
            snapshot with
            {
                CaseStatus = @case.Status.ToString(),
                ContradictionStatus = contradiction.Status.ToString()
            },
            cancellationToken);
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

# DI patch
$diPath = Join-Path $infra "Extensions\ServiceCollectionExtensions.cs"
if (Test-Path $diPath) {
    $diContent = Get-Content $diPath -Raw
    if ($diContent -notmatch 'ScenarioPersistenceService') {
        $diContent = $diContent -replace '(services\.AddSingleton<PersistentWorkflowAuditService>\(\);)', '$1
        services.AddSingleton<ScenarioPersistenceService>();'
        Write-File -Path $diPath -Content $diContent
    }
}

# Frontend persistence APIs + pages
Write-File -Path (Join-Path $web "api\persistence.ts") -Content @'
import { getAuthHeaders } from "./httpAuth";

export type ScenarioSnapshotResponse = {
  exists: boolean;
  caseId?: string | null;
  claimAId?: string | null;
  claimBId?: string | null;
  contradictionId?: string | null;
  createdAtUtc?: string | null;
  caseStatus?: string | null;
  contradictionStatus?: string | null;
  timestampUtc: string;
};

export type ResetResponse = {
  success: boolean;
  message: string;
  timestampUtc: string;
};

export async function getScenarioSnapshot(): Promise<ScenarioSnapshotResponse> {
  const response = await fetch("/api/v1/persistence/snapshot", {
    headers: getAuthHeaders(),
  });

  if (!response.ok) {
    const text = await response.text();
    throw new Error(text || `HTTP ${response.status}`);
  }

  return response.json() as Promise<ScenarioSnapshotResponse>;
}

export async function resetScenarioState(): Promise<ResetResponse> {
  const response = await fetch("/api/v1/persistence/reset", {
    method: "POST",
    headers: getAuthHeaders(),
  });

  if (!response.ok) {
    const text = await response.text();
    throw new Error(text || `HTTP ${response.status}`);
  }

  return response.json() as Promise<ResetResponse>;
}
'@

Write-File -Path (Join-Path $web "hooks\usePersistence.ts") -Content @'
import { useMutation, useQuery, useQueryClient } from "@tanstack/react-query";
import { getScenarioSnapshot, resetScenarioState } from "../api/persistence";

export function useScenarioSnapshot() {
  return useQuery({
    queryKey: ["scenario-snapshot"],
    queryFn: () => getScenarioSnapshot(),
  });
}

export function useResetScenarioState() {
  const queryClient = useQueryClient();

  return useMutation({
    mutationFn: () => resetScenarioState(),
    onSuccess: async () => {
      await queryClient.invalidateQueries({ queryKey: ["scenario-snapshot"] });
      await queryClient.invalidateQueries({ queryKey: ["workflow-audit-entries"] });
      await queryClient.invalidateQueries({ queryKey: ["case-explorer"] });
      await queryClient.invalidateQueries({ queryKey: ["cases"] });
      await queryClient.invalidateQueries({ queryKey: ["claims"] });
      await queryClient.invalidateQueries({ queryKey: ["contradictions"] });
    },
  });
}
'@

Write-File -Path (Join-Path $web "components\ScenarioSnapshotPanel.tsx") -Content @'
import type { ScenarioSnapshotResponse } from "../api/persistence";

export function ScenarioSnapshotPanel({
  snapshot,
}: {
  snapshot: ScenarioSnapshotResponse;
}) {
  return (
    <div style={panelStyle}>
      <h3 style={{ marginTop: 0 }}>Scenario Snapshot</h3>
      {!snapshot.exists && <p style={{ marginBottom: 0 }}>No snapshot found.</p>}
      {snapshot.exists && (
        <ul style={{ marginBottom: 0 }}>
          <li>Case Id: {snapshot.caseId}</li>
          <li>Claim A Id: {snapshot.claimAId}</li>
          <li>Claim B Id: {snapshot.claimBId}</li>
          <li>Contradiction Id: {snapshot.contradictionId}</li>
          <li>Created: {snapshot.createdAtUtc}</li>
          <li>Case Status: {snapshot.caseStatus}</li>
          <li>Contradiction Status: {snapshot.contradictionStatus}</li>
        </ul>
      )}
    </div>
  );
}

const panelStyle: React.CSSProperties = {
  border: "1px solid #ddd",
  borderRadius: 14,
  padding: 16,
};
'@

Write-File -Path (Join-Path $web "pages\PersistenceConsolePage.tsx") -Content @'
import { ScenarioSnapshotPanel } from "../components/ScenarioSnapshotPanel";
import { useResetScenarioState, useScenarioSnapshot } from "../hooks/usePersistence";

export function PersistenceConsolePage() {
  const snapshotQuery = useScenarioSnapshot();
  const resetMutation = useResetScenarioState();

  if (snapshotQuery.isLoading) {
    return <div style={{ fontFamily: "Arial, sans-serif", padding: 24 }}>Loading persistence console...</div>;
  }

  if (snapshotQuery.isError || !snapshotQuery.data) {
    return <div style={{ fontFamily: "Arial, sans-serif", padding: 24, color: "crimson" }}>Failed to load persistence console.</div>;
  }

  const error = (resetMutation.error as Error | null)?.message;

  return (
    <div style={{ fontFamily: "Arial, sans-serif", padding: 24 }}>
      <h1 style={{ marginTop: 0 }}>Persistence Console</h1>
      <p style={{ color: "#555" }}>
        Snapshot and reset surface for the persisted seeded lifecycle scenario.
      </p>

      <div style={{ display: "grid", gap: 16 }}>
        <ScenarioSnapshotPanel snapshot={snapshotQuery.data} />

        <div style={panelStyle}>
          <button
            onClick={() => resetMutation.mutate()}
            disabled={resetMutation.isPending}
            style={buttonStyle}
          >
            {resetMutation.isPending ? "Resetting..." : "Reset Scenario State"}
          </button>

          {resetMutation.data && <p style={{ margin: 0 }}>{resetMutation.data.message}</p>}
          {error && <p style={{ margin: 0, color: "crimson" }}>{error}</p>}
        </div>
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

const buttonStyle: React.CSSProperties = {
  border: "1px solid #bbb",
  borderRadius: 10,
  padding: "10px 14px",
  background: "white",
  cursor: "pointer",
  font: "inherit",
};
'@

Write-File -Path (Join-Path $web "pages\Phase12DataCenterPage.tsx") -Content @'
import { Link } from "react-router-dom";

export function Phase12DataCenterPage() {
  return (
    <div style={{ fontFamily: "Arial, sans-serif", padding: 24 }}>
      <h1 style={{ marginTop: 0 }}>Phase 12 Data Center</h1>
      <p style={{ color: "#555" }}>
        Data persistence, reset tooling, diagnostics, and integration verification hub.
      </p>

      <div style={gridStyle}>
        <Link to="/persistence-console" style={cardStyle}>Persistence Console</Link>
        <Link to="/lifecycle-seed" style={cardStyle}>Lifecycle Seed</Link>
        <Link to="/seeded-lifecycle-runner" style={cardStyle}>Seeded Lifecycle Runner</Link>
        <Link to="/workflow-audit" style={cardStyle}>Workflow Audit</Link>
        <Link to="/integration-test-center" style={cardStyle}>Integration Test Center</Link>
        <Link to="/phase-11-auth-center" style={cardStyle}>Phase 11 Auth</Link>
      </div>
    </div>
  );
}

const gridStyle: React.CSSProperties = {
  display: "grid",
  gridTemplateColumns: "repeat(auto-fit, minmax(220px, 1fr))",
  gap: 16,
};

const cardStyle: React.CSSProperties = {
  border: "1px solid #ddd",
  borderRadius: 14,
  padding: 16,
  textDecoration: "none",
  color: "inherit",
};
'@

# main.tsx patch
$main = Join-Path $web "main.tsx"
if (Test-Path $main) {
    $mainContent = Get-Content $main -Raw

    $mainContent = Ensure-ImportLine -Content $mainContent -Anchor 'import { DashboardPage } from "./pages/DashboardPage";' -ImportLine 'import { PersistenceConsolePage } from "./pages/PersistenceConsolePage";'
    $mainContent = Ensure-ImportLine -Content $mainContent -Anchor 'import { PersistenceConsolePage } from "./pages/PersistenceConsolePage";' -ImportLine 'import { Phase12DataCenterPage } from "./pages/Phase12DataCenterPage";'

    $mainContent = Ensure-RouteBlock -Content $mainContent -AnchorRoute '{ path: "/dashboard", element: <DashboardPage /> },' -RouteBlock '{ path: "/persistence-console", element: <PersistenceConsolePage /> },
  { path: "/phase-12-data-center", element: <Phase12DataCenterPage /> },' -PresencePattern 'path: "/persistence-console"'

    $mainContent = Ensure-NavBlock -Content $mainContent -Anchor '<Link to="/workflow-audit">Workflow Audit</Link>' -NavBlock '<Link to="/persistence-console">Persistence Console</Link>
          <Link to="/phase-12-data-center">Phase 12 Data</Link>' -PresencePattern 'to="/persistence-console"'

    Write-File -Path $main -Content $mainContent
}

# comprehensive automatic tests
Write-File -Path (Join-Path $tools "tests\Run-Phase12-Verification.ps1") -Content @'
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
Add-Content -Path $report -Value ""

$apiProcess = $null
$failed = $false

Push-Location (Join-Path $RootDir "apps\api\VeritasAtlas.Api")
try {
    $apiProcess = Start-Process "dotnet" -ArgumentList "run" -RedirectStandardOutput $stdoutLog -RedirectStandardError $stderrLog -PassThru
    Start-Sleep -Seconds 8

    $login = Invoke-Api -Url "$BaseUrl/api/v1/auth/login" -Method POST -Body @{ username = "admin1"; password = "password123" }
    Add-Result -ReportPath $report -Name "Login works" -Passed $login.Success -Detail $login.Message
    if (-not $login.Success) { $failed = $true; throw "Login failed." }

    $headers = @{ Authorization = "Bearer $($login.Data.accessToken)" }

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
'@

Write-File -Path (Join-Path $diag "phase-12-summary.md") -Content @'
# Phase 12 Summary

## Included
- scenario snapshot persistence service
- persistence controller with snapshot and reset endpoints
- workflow transition service snapshot integration
- frontend persistence API, hooks, snapshot panel, and persistence console
- phase 12 data center page
- comprehensive automatic Phase 12 verification runner

## Goal
Strengthen persistence, scenario reset tooling, diagnostics visibility, and seeded lifecycle verification.
'@

Write-Host "Building..." -ForegroundColor Cyan
Build-All -RootDir $RootDir

Write-Host "Running Phase 12 verification..." -ForegroundColor Cyan
Invoke-Phase12Tests -RootDir $RootDir

Write-Host "Phase 12 bundle DONE" -ForegroundColor Green
