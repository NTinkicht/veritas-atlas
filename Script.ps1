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
    if (Test-Path $web) {
        Push-Location $web
        try {
            npm run build
            if ($LASTEXITCODE -ne 0) { throw "Frontend failed" }
        }
        finally {
            Pop-Location
        }
    }
}

function Replace-InFile {
    param(
        [string]$Path,
        [string]$Pattern,
        [string]$Replacement
    )

    if (-not (Test-Path $Path)) {
        throw "File not found: $Path"
    }

    $content = Get-Content $Path -Raw
    $updated = [regex]::Replace($content, $Pattern, $Replacement, [System.Text.RegularExpressions.RegexOptions]::Singleline)
    Write-File -Path $Path -Content $updated
}

Write-Host "Checkpointing current code with git..." -ForegroundColor Cyan
Git-Checkpoint -Message ("checkpoint before phase 8.0 - " + (Get-Date -Format "yyyy-MM-dd HH:mm:ss"))

Write-Host "Applying Phase 8.0 - persisted workflow transitions and seeded lifecycle..." -ForegroundColor Cyan

$api = Join-Path $RootDir "apps\api"
$infra = Join-Path $api "VeritasAtlas.Infrastructure"
$app = Join-Path $api "VeritasAtlas.Application"
$domain = Join-Path $api "VeritasAtlas.Domain"
$apiProj = Join-Path $api "VeritasAtlas.Api"
$diag = Join-Path $RootDir "_diagnostics\phase-8-0"
Ensure-Dir $diag

# =========================================
# Contracts and service for persisted transitions
# =========================================

Write-File (Join-Path $apiProj "Contracts\Workflow\WorkflowContracts.cs") @'
namespace VeritasAtlas.Api.Contracts.Workflow;

public sealed record WorkflowTransitionResponse(
    string EntityType,
    Guid EntityId,
    string Status,
    DateTime TimestampUtc,
    string Message);

public sealed record WorkflowSeedResponse(
    Guid CaseId,
    Guid PrimaryClaimId,
    Guid SecondaryClaimId,
    Guid ContradictionId,
    string CaseStatus,
    string ContradictionStatus,
    DateTime TimestampUtc);
'@

Write-File (Join-Path $infra "Services\WorkflowTransitionService.cs") @'
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
        var entity = await _dbContext.Cases.FirstOrDefaultAsync(x => x.Id == caseId, cancellationToken)
            ?? throw new InvalidOperationException($"Case '{caseId}' was not found.");

        entity.Status = ParseCaseStatus("InReview", entity.Status);
        Touch(entity);
        await _dbContext.SaveChangesAsync(cancellationToken);
        return (entity.Id, entity.Status.ToString());
    }

    public async Task<(Guid Id, string Status)> ApproveCaseAsync(Guid caseId, CancellationToken cancellationToken = default)
    {
        var entity = await _dbContext.Cases.FirstOrDefaultAsync(x => x.Id == caseId, cancellationToken)
            ?? throw new InvalidOperationException($"Case '{caseId}' was not found.");

        entity.Status = ParseCaseStatus("Approved", entity.Status);
        Touch(entity);
        await _dbContext.SaveChangesAsync(cancellationToken);
        return (entity.Id, entity.Status.ToString());
    }

    public async Task<(Guid Id, string Status)> RejectCaseAsync(Guid caseId, CancellationToken cancellationToken = default)
    {
        var entity = await _dbContext.Cases.FirstOrDefaultAsync(x => x.Id == caseId, cancellationToken)
            ?? throw new InvalidOperationException($"Case '{caseId}' was not found.");

        entity.Status = ParseCaseStatus("Rejected", entity.Status);
        Touch(entity);
        await _dbContext.SaveChangesAsync(cancellationToken);
        return (entity.Id, entity.Status.ToString());
    }

    public async Task<(Guid Id, string Status)> PreparePublicationAsync(Guid caseId, CancellationToken cancellationToken = default)
    {
        var entity = await _dbContext.Cases.FirstOrDefaultAsync(x => x.Id == caseId, cancellationToken)
            ?? throw new InvalidOperationException($"Case '{caseId}' was not found.");

        entity.Status = ParseCaseStatus("ReadyForPublication", entity.Status);
        Touch(entity);
        await _dbContext.SaveChangesAsync(cancellationToken);
        return (entity.Id, entity.Status.ToString());
    }

    public async Task<(Guid Id, string Status)> PublishCaseAsync(Guid caseId, CancellationToken cancellationToken = default)
    {
        var entity = await _dbContext.Cases.FirstOrDefaultAsync(x => x.Id == caseId, cancellationToken)
            ?? throw new InvalidOperationException($"Case '{caseId}' was not found.");

        entity.Status = ParseCaseStatus("Published", entity.Status);
        Touch(entity);
        await _dbContext.SaveChangesAsync(cancellationToken);
        return (entity.Id, entity.Status.ToString());
    }

    public async Task<(Guid Id, string Status)> HoldCaseAsync(Guid caseId, CancellationToken cancellationToken = default)
    {
        var entity = await _dbContext.Cases.FirstOrDefaultAsync(x => x.Id == caseId, cancellationToken)
            ?? throw new InvalidOperationException($"Case '{caseId}' was not found.");

        entity.Status = ParseCaseStatus("OnHold", entity.Status);
        Touch(entity);
        await _dbContext.SaveChangesAsync(cancellationToken);
        return (entity.Id, entity.Status.ToString());
    }

    public async Task<(Guid Id, string Status)> ResolveContradictionAsync(Guid contradictionId, CancellationToken cancellationToken = default)
    {
        var entity = await _dbContext.Contradictions.FirstOrDefaultAsync(x => x.Id == contradictionId, cancellationToken)
            ?? throw new InvalidOperationException($"Contradiction '{contradictionId}' was not found.");

        entity.Status = ContradictionStatus.Resolved;
        Touch(entity);
        await _dbContext.SaveChangesAsync(cancellationToken);
        return (entity.Id, entity.Status.ToString());
    }

    public async Task<(Guid Id, string Status)> EscalateContradictionAsync(Guid contradictionId, CancellationToken cancellationToken = default)
    {
        var entity = await _dbContext.Contradictions.FirstOrDefaultAsync(x => x.Id == contradictionId, cancellationToken)
            ?? throw new InvalidOperationException($"Contradiction '{contradictionId}' was not found.");

        entity.Status = ParseContradictionStatus("UnderReview", entity.Status);
        Touch(entity);
        await _dbContext.SaveChangesAsync(cancellationToken);
        return (entity.Id, entity.Status.ToString());
    }

    public async Task<(Guid Id, string Status)> SendClaimToReviewAsync(Guid claimId, CancellationToken cancellationToken = default)
    {
        var entity = await _dbContext.Claims.FirstOrDefaultAsync(x => x.Id == claimId, cancellationToken)
            ?? throw new InvalidOperationException($"Claim '{claimId}' was not found.");

        entity.Status = ParseClaimStatus("InReview", entity.Status);
        Touch(entity);
        await _dbContext.SaveChangesAsync(cancellationToken);
        return (entity.Id, entity.Status.ToString());
    }

    public async Task<(Guid Id, string Status)> ReturnClaimForEditAsync(Guid claimId, CancellationToken cancellationToken = default)
    {
        var entity = await _dbContext.Claims.FirstOrDefaultAsync(x => x.Id == claimId, cancellationToken)
            ?? throw new InvalidOperationException($"Claim '{claimId}' was not found.");

        entity.Status = ParseClaimStatus("Draft", entity.Status);
        Touch(entity);
        await _dbContext.SaveChangesAsync(cancellationToken);
        return (entity.Id, entity.Status.ToString());
    }

    public async Task<(Guid Id, string Status)> CompleteReviewAsync(Guid reviewId, CancellationToken cancellationToken = default)
    {
        var entity = await _dbContext.Reviews.FirstOrDefaultAsync(x => x.Id == reviewId, cancellationToken)
            ?? throw new InvalidOperationException($"Review '{reviewId}' was not found.");

        entity.Status = ParseReviewStatus("Completed", entity.Status);
        Touch(entity);
        await _dbContext.SaveChangesAsync(cancellationToken);
        return (entity.Id, entity.Status.ToString());
    }

    public async Task<(Guid Id, string Status)> ReopenReviewAsync(Guid reviewId, CancellationToken cancellationToken = default)
    {
        var entity = await _dbContext.Reviews.FirstOrDefaultAsync(x => x.Id == reviewId, cancellationToken)
            ?? throw new InvalidOperationException($"Review '{reviewId}' was not found.");

        entity.Status = ParseReviewStatus("Open", entity.Status);
        Touch(entity);
        await _dbContext.SaveChangesAsync(cancellationToken);
        return (entity.Id, entity.Status.ToString());
    }

    public async Task<(Guid CaseId, Guid ClaimAId, Guid ClaimBId, Guid ContradictionId, string CaseStatus, string ContradictionStatus)> SeedLifecycleAsync(CancellationToken cancellationToken = default)
    {
        var @case = new Case
        {
            Id = Guid.NewGuid(),
            Title = "Phase 8 Seed Case",
            Description = "Seeded lifecycle case for workflow verification",
            Status = ParseCaseStatus("Draft", default),
            CreatedAtUtc = DateTime.UtcNow,
            UpdatedAtUtc = DateTime.UtcNow
        };

        var claimA = new Claim
        {
            Id = Guid.NewGuid(),
            CaseId = @case.Id,
            Topic = "Seed Claim A",
            NormalizedText = "Seed claim A normalized text",
            Status = ParseClaimStatus("Draft", default),
            CreatedAtUtc = DateTime.UtcNow,
            UpdatedAtUtc = DateTime.UtcNow
        };

        var claimB = new Claim
        {
            Id = Guid.NewGuid(),
            CaseId = @case.Id,
            Topic = "Seed Claim B",
            NormalizedText = "Seed claim B normalized text",
            Status = ParseClaimStatus("Draft", default),
            CreatedAtUtc = DateTime.UtcNow,
            UpdatedAtUtc = DateTime.UtcNow
        };

        var contradiction = new Contradiction
        {
            Id = Guid.NewGuid(),
            CaseId = @case.Id,
            LeftClaimId = claimA.Id,
            RightClaimId = claimB.Id,
            Type = ContradictionType.Direct,
            Severity = ContradictionSeverity.Medium,
            Status = ContradictionStatus.Draft,
            Summary = "Seed contradiction",
            CreatedAtUtc = DateTime.UtcNow,
            UpdatedAtUtc = DateTime.UtcNow
        };

        _dbContext.Cases.Add(@case);
        _dbContext.Claims.Add(claimA);
        _dbContext.Claims.Add(claimB);
        _dbContext.Contradictions.Add(contradiction);
        await _dbContext.SaveChangesAsync(cancellationToken);

        return (@case.Id, claimA.Id, claimB.Id, contradiction.Id, @case.Status.ToString(), contradiction.Status.ToString());
    }

    private static void Touch(object entity)
    {
        var type = entity.GetType();

        var updatedAt = type.GetProperty("UpdatedAtUtc");
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

        var names = Enum.GetNames(typeof(TEnum));
        var exact = names.FirstOrDefault(x => string.Equals(x, desired, StringComparison.OrdinalIgnoreCase));
        if (exact is not null && Enum.TryParse<TEnum>(exact, true, out parsed))
        {
            return parsed;
        }

        return fallback;
    }

    private static dynamic ParseCaseStatus(string desired, dynamic fallback)
    {
        return ParseEnum(desired, fallback);
    }

    private static dynamic ParseClaimStatus(string desired, dynamic fallback)
    {
        return ParseEnum(desired, fallback);
    }

    private static dynamic ParseReviewStatus(string desired, dynamic fallback)
    {
        return ParseEnum(desired, fallback);
    }

    private static ContradictionStatus ParseContradictionStatus(string desired, ContradictionStatus fallback)
    {
        return ParseEnum(desired, fallback);
    }
}
'@

# Wire DI
$diPath = Join-Path $infra "Extensions\ServiceCollectionExtensions.cs"
if (Test-Path $diPath) {
    $diContent = Get-Content $diPath -Raw
    if ($diContent -notmatch 'WorkflowTransitionService') {
        $diContent = $diContent -replace '(services\.AddScoped<ContradictionSliceService>\(\);)', '$1
        services.AddScoped<WorkflowTransitionService>();'
        Write-File $diPath $diContent
    }
}

# Replace stub actions controller with persisted version
Write-File (Join-Path $apiProj "Controllers\ActionsController.cs") @'
using Microsoft.AspNetCore.Mvc;
using VeritasAtlas.Api.Contracts.Workflow;
using VeritasAtlas.Infrastructure.Services;

namespace VeritasAtlas.Api.Controllers;

[ApiController]
[Route("api/v1/actions")]
public class ActionsController : ControllerBase
{
    private readonly WorkflowTransitionService _workflowTransitionService;

    public ActionsController(WorkflowTransitionService workflowTransitionService)
    {
        _workflowTransitionService = workflowTransitionService;
    }

    [HttpPost("cases/{caseId}/submit")]
    public async Task<ActionResult<WorkflowTransitionResponse>> SubmitCase(Guid caseId, CancellationToken cancellationToken)
    {
        var result = await _workflowTransitionService.SubmitCaseAsync(caseId, cancellationToken);
        return Ok(new WorkflowTransitionResponse("Case", result.Id, result.Status, DateTime.UtcNow, "Case submitted."));
    }

    [HttpPost("cases/{caseId}/approve")]
    public async Task<ActionResult<WorkflowTransitionResponse>> ApproveCase(Guid caseId, CancellationToken cancellationToken)
    {
        var result = await _workflowTransitionService.ApproveCaseAsync(caseId, cancellationToken);
        return Ok(new WorkflowTransitionResponse("Case", result.Id, result.Status, DateTime.UtcNow, "Case approved."));
    }

    [HttpPost("cases/{caseId}/reject")]
    public async Task<ActionResult<WorkflowTransitionResponse>> RejectCase(Guid caseId, CancellationToken cancellationToken)
    {
        var result = await _workflowTransitionService.RejectCaseAsync(caseId, cancellationToken);
        return Ok(new WorkflowTransitionResponse("Case", result.Id, result.Status, DateTime.UtcNow, "Case rejected."));
    }

    [HttpPost("contradictions/{id}/resolve")]
    public async Task<ActionResult<WorkflowTransitionResponse>> ResolveContradiction(Guid id, CancellationToken cancellationToken)
    {
        var result = await _workflowTransitionService.ResolveContradictionAsync(id, cancellationToken);
        return Ok(new WorkflowTransitionResponse("Contradiction", result.Id, result.Status, DateTime.UtcNow, "Contradiction resolved."));
    }

    [HttpPost("reviews/{id}/complete")]
    public async Task<ActionResult<WorkflowTransitionResponse>> CompleteReview(Guid id, CancellationToken cancellationToken)
    {
        var result = await _workflowTransitionService.CompleteReviewAsync(id, cancellationToken);
        return Ok(new WorkflowTransitionResponse("Review", result.Id, result.Status, DateTime.UtcNow, "Review completed."));
    }

    [HttpPost("seed/lifecycle")]
    public async Task<ActionResult<WorkflowSeedResponse>> SeedLifecycle(CancellationToken cancellationToken)
    {
        var result = await _workflowTransitionService.SeedLifecycleAsync(cancellationToken);

        return Ok(new WorkflowSeedResponse(
            result.CaseId,
            result.ClaimAId,
            result.ClaimBId,
            result.ContradictionId,
            result.CaseStatus,
            result.ContradictionStatus,
            DateTime.UtcNow));
    }
}
'@

Write-File (Join-Path $apiProj "Controllers\ReviewWorkflowController.cs") @'
using Microsoft.AspNetCore.Mvc;
using VeritasAtlas.Api.Contracts.Workflow;
using VeritasAtlas.Infrastructure.Services;

namespace VeritasAtlas.Api.Controllers;

[ApiController]
[Route("api/v1/review-workflow")]
public class ReviewWorkflowController : ControllerBase
{
    private readonly WorkflowTransitionService _workflowTransitionService;

    public ReviewWorkflowController(WorkflowTransitionService workflowTransitionService)
    {
        _workflowTransitionService = workflowTransitionService;
    }

    [HttpPost("claims/{claimId}/send-to-review")]
    public async Task<ActionResult<WorkflowTransitionResponse>> SendClaimToReview(Guid claimId, CancellationToken cancellationToken)
    {
        var result = await _workflowTransitionService.SendClaimToReviewAsync(claimId, cancellationToken);
        return Ok(new WorkflowTransitionResponse("Claim", result.Id, result.Status, DateTime.UtcNow, "Claim sent to review."));
    }

    [HttpPost("claims/{claimId}/return-for-edit")]
    public async Task<ActionResult<WorkflowTransitionResponse>> ReturnClaimForEdit(Guid claimId, CancellationToken cancellationToken)
    {
        var result = await _workflowTransitionService.ReturnClaimForEditAsync(claimId, cancellationToken);
        return Ok(new WorkflowTransitionResponse("Claim", result.Id, result.Status, DateTime.UtcNow, "Claim returned for edit."));
    }

    [HttpPost("contradictions/{id}/escalate")]
    public async Task<ActionResult<WorkflowTransitionResponse>> EscalateContradiction(Guid id, CancellationToken cancellationToken)
    {
        var result = await _workflowTransitionService.EscalateContradictionAsync(id, cancellationToken);
        return Ok(new WorkflowTransitionResponse("Contradiction", result.Id, result.Status, DateTime.UtcNow, "Contradiction escalated."));
    }

    [HttpPost("reviews/{id}/reopen")]
    public async Task<ActionResult<WorkflowTransitionResponse>> ReopenReview(Guid id, CancellationToken cancellationToken)
    {
        var result = await _workflowTransitionService.ReopenReviewAsync(id, cancellationToken);
        return Ok(new WorkflowTransitionResponse("Review", result.Id, result.Status, DateTime.UtcNow, "Review reopened."));
    }
}
'@

Write-File (Join-Path $apiProj "Controllers\PublicationWorkflowController.cs") @'
using Microsoft.AspNetCore.Mvc;
using VeritasAtlas.Api.Contracts.Workflow;
using VeritasAtlas.Infrastructure.Services;

namespace VeritasAtlas.Api.Controllers;

[ApiController]
[Route("api/v1/publication-workflow")]
public class PublicationWorkflowController : ControllerBase
{
    private readonly WorkflowTransitionService _workflowTransitionService;

    public PublicationWorkflowController(WorkflowTransitionService workflowTransitionService)
    {
        _workflowTransitionService = workflowTransitionService;
    }

    [HttpPost("cases/{caseId}/prepare")]
    public async Task<ActionResult<WorkflowTransitionResponse>> PreparePublication(Guid caseId, CancellationToken cancellationToken)
    {
        var result = await _workflowTransitionService.PreparePublicationAsync(caseId, cancellationToken);
        return Ok(new WorkflowTransitionResponse("Case", result.Id, result.Status, DateTime.UtcNow, "Case prepared for publication."));
    }

    [HttpPost("cases/{caseId}/publish")]
    public async Task<ActionResult<WorkflowTransitionResponse>> PublishCase(Guid caseId, CancellationToken cancellationToken)
    {
        var result = await _workflowTransitionService.PublishCaseAsync(caseId, cancellationToken);
        return Ok(new WorkflowTransitionResponse("Case", result.Id, result.Status, DateTime.UtcNow, "Case published."));
    }

    [HttpPost("cases/{caseId}/hold")]
    public async Task<ActionResult<WorkflowTransitionResponse>> HoldCase(Guid caseId, CancellationToken cancellationToken)
    {
        var result = await _workflowTransitionService.HoldCaseAsync(caseId, cancellationToken);
        return Ok(new WorkflowTransitionResponse("Case", result.Id, result.Status, DateTime.UtcNow, "Case put on hold."));
    }
}
'@

# Frontend helpers for seeding lifecycle
Write-File (Join-Path $web "api\workflowSeed.ts") @'
export type WorkflowSeedResponse = {
  caseId: string;
  primaryClaimId: string;
  secondaryClaimId: string;
  contradictionId: string;
  caseStatus: string;
  contradictionStatus: string;
  timestampUtc: string;
};

export async function seedWorkflowLifecycle(): Promise<WorkflowSeedResponse> {
  const response = await fetch("/api/v1/actions/seed/lifecycle", { method: "POST" });

  if (!response.ok) {
    const text = await response.text();
    throw new Error(text || `HTTP ${response.status}`);
  }

  return response.json() as Promise<WorkflowSeedResponse>;
}
'@

Write-File (Join-Path $web "hooks\useWorkflowSeed.ts") @'
import { useMutation, useQueryClient } from "@tanstack/react-query";
import { seedWorkflowLifecycle } from "../api/workflowSeed";

export function useWorkflowSeed() {
  const queryClient = useQueryClient();

  return useMutation({
    mutationFn: () => seedWorkflowLifecycle(),
    onSuccess: () => {
      queryClient.invalidateQueries({ queryKey: ["case-explorer"] });
      queryClient.invalidateQueries({ queryKey: ["cases"] });
      queryClient.invalidateQueries({ queryKey: ["claims"] });
      queryClient.invalidateQueries({ queryKey: ["contradictions"] });
    },
  });
}
'@

Write-File (Join-Path $web "components\SeedScenarioPanel.tsx") @'
export function SeedScenarioPanel({
  onSeed,
  isPending,
  message,
}: {
  onSeed: () => void;
  isPending: boolean;
  message?: string;
}) {
  return (
    <div style={panelStyle}>
      <h3 style={{ marginTop: 0 }}>Seed Scenario</h3>
      <button onClick={onSeed} disabled={isPending} style={buttonStyle}>
        {isPending ? "Seeding..." : "Seed Lifecycle Scenario"}
      </button>
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

Write-File (Join-Path $web "pages\LifecycleSeedPage.tsx") @'
import { SeedScenarioPanel } from "../components/SeedScenarioPanel";
import { useWorkflowSeed } from "../hooks/useWorkflowSeed";

export function LifecycleSeedPage() {
  const seedMutation = useWorkflowSeed();

  const message = seedMutation.data
    ? `Case ${seedMutation.data.caseId} seeded with contradiction ${seedMutation.data.contradictionId}.`
    : undefined;

  return (
    <div style={{ fontFamily: "Arial, sans-serif", padding: 24 }}>
      <h1 style={{ marginTop: 0 }}>Lifecycle Seed</h1>
      <p style={{ color: "#555" }}>
        Seed a minimal end-to-end workflow scenario for validation and UI interaction.
      </p>

      <SeedScenarioPanel
        onSeed={() => seedMutation.mutate()}
        isPending={seedMutation.isPending}
        message={message}
      />
    </div>
  );
}
'@

$main = Join-Path $web "main.tsx"
$content = Get-Content $main -Raw

if ($content -notmatch 'import \{ LifecycleSeedPage \} from "\./pages/LifecycleSeedPage";') {
    $content = $content -replace 'import \{ DashboardPage \} from "\./pages/DashboardPage";', @'
import { DashboardPage } from "./pages/DashboardPage";
import { LifecycleSeedPage } from "./pages/LifecycleSeedPage";
'@
}

$content = Ensure-RouteBlock -Content $content -AnchorRoute '{ path: "/dashboard", element: <DashboardPage /> },' -RouteBlock '{ path: "/lifecycle-seed", element: <LifecycleSeedPage /> },' -PresencePattern 'path: "/lifecycle-seed"'
$content = Ensure-NavBlock -Content $content -Anchor '<Link to="/workflow-diagnostics">Workflow Diagnostics</Link>' -NavBlock '<Link to="/lifecycle-seed">Lifecycle Seed</Link>' -PresencePattern 'to="/lifecycle-seed"'
Write-File $main $content

Write-File (Join-Path $diag "phase-8-0-summary.md") @'
# Phase 8.0 Summary

## Added
- persisted workflow transition service
- actions controller wired to persistence
- review workflow controller wired to persistence
- publication workflow controller wired to persistence
- lifecycle seed endpoint
- frontend seed page and hook

## Goal
Convert workflow actions from stubbed responses into persisted database state transitions and provide a repeatable seeded scenario.

## Next
- verify enum names against real domain if needed
- add business rule validation
- add authorization
- add integration tests against seeded scenario
'@

Write-Host "Building..." -ForegroundColor Cyan
Build-All -RootDir $RootDir

Write-Host "Phase 8.0 DONE" -ForegroundColor Green
