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

function Invoke-Phase9Tests {
    param(
        [Parameter(Mandatory = $true)][string]$RootDir
    )

    $runner = Join-Path $RootDir "tools\tests\Run-Phase9-Verification.ps1"
    if (-not (Test-Path $runner)) {
        throw "Phase 9 test runner not found: $runner"
    }

    Push-Location $RootDir
    try {
        & powershell -ExecutionPolicy Bypass -File $runner -RootDir $RootDir
        if ($LASTEXITCODE -ne 0) {
            throw "Phase 9 verification runner failed."
        }
    }
    finally {
        Pop-Location
    }
}

Write-Host "Checkpointing current code with git..." -ForegroundColor Cyan
Git-Checkpoint -Message ("checkpoint before phase 9 bundle - " + (Get-Date -Format "yyyy-MM-dd HH:mm:ss"))

Write-Host "Applying Phase 9 bundle - workflow integrity, validation, diagnostics, roles, and reliability..." -ForegroundColor Cyan

$api = Join-Path $RootDir "apps\api"
$infra = Join-Path $api "VeritasAtlas.Infrastructure"
$apiProj = Join-Path $api "VeritasAtlas.Api"
$web = Join-Path $RootDir "apps\web\veritas-atlas-web\src"
$tools = Join-Path $RootDir "tools"
$diag = Join-Path $RootDir "_diagnostics\phase-9"
Ensure-Dir -Path $diag

# =========================
# BACKEND: contracts
# =========================

Write-File -Path (Join-Path $apiProj "Contracts\Workflow\WorkflowAuditContracts.cs") -Content @'
namespace VeritasAtlas.Api.Contracts.Workflow;

public sealed record WorkflowAuditEntryResponse(
    Guid Id,
    string EntityType,
    Guid EntityId,
    string ActionName,
    string? PreviousStatus,
    string NextStatus,
    string Role,
    bool Success,
    string Message,
    DateTime TimestampUtc);

public sealed record WorkflowValidationFailureResponse(
    string EntityType,
    Guid EntityId,
    string RequestedAction,
    string Message,
    DateTime TimestampUtc);
'@

# =========================
# BACKEND: audit + integrity services
# =========================

Write-File -Path (Join-Path $infra "Services\WorkflowAuditStore.cs") -Content @'
using System.Collections.Concurrent;

namespace VeritasAtlas.Infrastructure.Services;

public sealed class WorkflowAuditStore
{
    private static readonly ConcurrentQueue<WorkflowAuditRecord> _records = new();

    public void Add(WorkflowAuditRecord record)
    {
        _records.Enqueue(record);
    }

    public IReadOnlyList<WorkflowAuditRecord> GetAll()
    {
        return _records.ToArray()
            .OrderByDescending(x => x.TimestampUtc)
            .ToList();
    }

    public void Clear()
    {
        while (_records.TryDequeue(out _)) { }
    }
}

public sealed record WorkflowAuditRecord(
    Guid Id,
    string EntityType,
    Guid EntityId,
    string ActionName,
    string? PreviousStatus,
    string NextStatus,
    string Role,
    bool Success,
    string Message,
    DateTime TimestampUtc);
'@

Write-File -Path (Join-Path $infra "Services\WorkflowIntegrityService.cs") -Content @'
namespace VeritasAtlas.Infrastructure.Services;

public sealed class WorkflowIntegrityService
{
    private static readonly Dictionary<string, Dictionary<string, string[]>> AllowedTransitions =
        new(StringComparer.OrdinalIgnoreCase)
        {
            ["Case"] = new(StringComparer.OrdinalIgnoreCase)
            {
                ["Draft"] = new[] { "InReview" },
                ["InReview"] = new[] { "Approved", "Rejected", "ReadyForPublication", "OnHold" },
                ["Approved"] = new[] { "ReadyForPublication", "OnHold" },
                ["Rejected"] = new[] { "InReview" },
                ["ReadyForPublication"] = new[] { "Published", "OnHold" },
                ["OnHold"] = new[] { "ReadyForPublication", "InReview" },
                ["Published"] = Array.Empty<string>()
            },
            ["Claim"] = new(StringComparer.OrdinalIgnoreCase)
            {
                ["Draft"] = new[] { "InReview" },
                ["InReview"] = new[] { "Draft" }
            },
            ["Contradiction"] = new(StringComparer.OrdinalIgnoreCase)
            {
                ["Draft"] = new[] { "UnderReview", "Resolved" },
                ["UnderReview"] = new[] { "Resolved" },
                ["Resolved"] = Array.Empty<string>()
            },
            ["Review"] = new(StringComparer.OrdinalIgnoreCase)
            {
                ["Open"] = new[] { "Completed" },
                ["Completed"] = new[] { "Open" }
            }
        };

    private static readonly Dictionary<string, string[]> RolePolicies =
        new(StringComparer.OrdinalIgnoreCase)
        {
            ["SubmitCase"] = new[] { "operator", "reviewer", "admin" },
            ["ApproveCase"] = new[] { "reviewer", "admin" },
            ["RejectCase"] = new[] { "reviewer", "admin" },
            ["ResolveContradiction"] = new[] { "reviewer", "admin" },
            ["CompleteReview"] = new[] { "reviewer", "admin" },
            ["SendClaimToReview"] = new[] { "reviewer", "admin" },
            ["ReturnClaimForEdit"] = new[] { "reviewer", "admin" },
            ["EscalateContradiction"] = new[] { "reviewer", "admin" },
            ["ReopenReview"] = new[] { "reviewer", "admin" },
            ["PreparePublication"] = new[] { "reviewer", "publisher", "admin" },
            ["PublishCase"] = new[] { "publisher", "admin" },
            ["HoldCase"] = new[] { "publisher", "admin" },
            ["SeedLifecycle"] = new[] { "operator", "admin" }
        };

    public void EnsureRoleAllowed(string actionName, string? role)
    {
        var normalizedRole = string.IsNullOrWhiteSpace(role) ? "anonymous" : role.Trim().ToLowerInvariant();

        if (!RolePolicies.TryGetValue(actionName, out var roles))
        {
            return;
        }

        if (!roles.Contains(normalizedRole, StringComparer.OrdinalIgnoreCase))
        {
            throw new UnauthorizedAccessException($"Role '{normalizedRole}' is not allowed to execute '{actionName}'.");
        }
    }

    public void EnsureTransitionAllowed(string entityType, string? currentStatus, string nextStatus)
    {
        var current = string.IsNullOrWhiteSpace(currentStatus) ? "Draft" : currentStatus.Trim();

        if (!AllowedTransitions.TryGetValue(entityType, out var map))
        {
            return;
        }

        if (!map.TryGetValue(current, out var allowed))
        {
            throw new InvalidOperationException($"No transition rules are defined for {entityType} status '{current}'.");
        }

        if (!allowed.Contains(nextStatus, StringComparer.OrdinalIgnoreCase))
        {
            throw new InvalidOperationException($"{entityType} cannot transition from '{current}' to '{nextStatus}'.");
        }
    }

    public IReadOnlyDictionary<string, IReadOnlyDictionary<string, string[]>> GetRules()
    {
        return AllowedTransitions.ToDictionary(
            x => x.Key,
            x => (IReadOnlyDictionary<string, string[]>)x.Value,
            StringComparer.OrdinalIgnoreCase);
    }

    public IReadOnlyDictionary<string, string[]> GetRolePolicies()
    {
        return RolePolicies.ToDictionary(
            x => x.Key,
            x => x.Value,
            StringComparer.OrdinalIgnoreCase);
    }
}
'@

Write-File -Path (Join-Path $infra "Services\WorkflowOrchestratorService.cs") -Content @'
using Microsoft.EntityFrameworkCore;
using VeritasAtlas.Domain.Entities;
using VeritasAtlas.Infrastructure.Persistence;

namespace VeritasAtlas.Infrastructure.Services;

public sealed class WorkflowOrchestratorService
{
    private readonly VeritasAtlasDbContext _dbContext;
    private readonly WorkflowTransitionService _workflowTransitionService;
    private readonly WorkflowIntegrityService _workflowIntegrityService;
    private readonly WorkflowAuditStore _workflowAuditStore;

    public WorkflowOrchestratorService(
        VeritasAtlasDbContext dbContext,
        WorkflowTransitionService workflowTransitionService,
        WorkflowIntegrityService workflowIntegrityService,
        WorkflowAuditStore workflowAuditStore)
    {
        _dbContext = dbContext;
        _workflowTransitionService = workflowTransitionService;
        _workflowIntegrityService = workflowIntegrityService;
        _workflowAuditStore = workflowAuditStore;
    }

    public async Task<(Guid Id, string Status)> SubmitCaseAsync(Guid caseId, string? role, CancellationToken cancellationToken = default)
    {
        return await RunCaseActionAsync("SubmitCase", caseId, "InReview", role, _workflowTransitionService.SubmitCaseAsync, cancellationToken);
    }

    public async Task<(Guid Id, string Status)> ApproveCaseAsync(Guid caseId, string? role, CancellationToken cancellationToken = default)
    {
        return await RunCaseActionAsync("ApproveCase", caseId, "Approved", role, _workflowTransitionService.ApproveCaseAsync, cancellationToken);
    }

    public async Task<(Guid Id, string Status)> RejectCaseAsync(Guid caseId, string? role, CancellationToken cancellationToken = default)
    {
        return await RunCaseActionAsync("RejectCase", caseId, "Rejected", role, _workflowTransitionService.RejectCaseAsync, cancellationToken);
    }

    public async Task<(Guid Id, string Status)> PreparePublicationAsync(Guid caseId, string? role, CancellationToken cancellationToken = default)
    {
        return await RunCaseActionAsync("PreparePublication", caseId, "ReadyForPublication", role, _workflowTransitionService.PreparePublicationAsync, cancellationToken);
    }

    public async Task<(Guid Id, string Status)> PublishCaseAsync(Guid caseId, string? role, CancellationToken cancellationToken = default)
    {
        return await RunCaseActionAsync("PublishCase", caseId, "Published", role, _workflowTransitionService.PublishCaseAsync, cancellationToken);
    }

    public async Task<(Guid Id, string Status)> HoldCaseAsync(Guid caseId, string? role, CancellationToken cancellationToken = default)
    {
        return await RunCaseActionAsync("HoldCase", caseId, "OnHold", role, _workflowTransitionService.HoldCaseAsync, cancellationToken);
    }

    public async Task<(Guid Id, string Status)> ResolveContradictionAsync(Guid contradictionId, string? role, CancellationToken cancellationToken = default)
    {
        return await RunContradictionActionAsync("ResolveContradiction", contradictionId, "Resolved", role, _workflowTransitionService.ResolveContradictionAsync, cancellationToken);
    }

    public async Task<(Guid Id, string Status)> EscalateContradictionAsync(Guid contradictionId, string? role, CancellationToken cancellationToken = default)
    {
        return await RunContradictionActionAsync("EscalateContradiction", contradictionId, "UnderReview", role, _workflowTransitionService.EscalateContradictionAsync, cancellationToken);
    }

    public async Task<(Guid Id, string Status)> SendClaimToReviewAsync(Guid claimId, string? role, CancellationToken cancellationToken = default)
    {
        return await RunClaimActionAsync("SendClaimToReview", claimId, "InReview", role, _workflowTransitionService.SendClaimToReviewAsync, cancellationToken);
    }

    public async Task<(Guid Id, string Status)> ReturnClaimForEditAsync(Guid claimId, string? role, CancellationToken cancellationToken = default)
    {
        return await RunClaimActionAsync("ReturnClaimForEdit", claimId, "Draft", role, _workflowTransitionService.ReturnClaimForEditAsync, cancellationToken);
    }

    public async Task<(Guid Id, string Status)> CompleteReviewAsync(Guid reviewId, string? role, CancellationToken cancellationToken = default)
    {
        return await RunReviewActionAsync("CompleteReview", reviewId, "Completed", role, _workflowTransitionService.CompleteReviewAsync, cancellationToken);
    }

    public async Task<(Guid Id, string Status)> ReopenReviewAsync(Guid reviewId, string? role, CancellationToken cancellationToken = default)
    {
        return await RunReviewActionAsync("ReopenReview", reviewId, "Open", role, _workflowTransitionService.ReopenReviewAsync, cancellationToken);
    }

    public async Task<(Guid CaseId, Guid ClaimAId, Guid ClaimBId, Guid ContradictionId, string CaseStatus, string ContradictionStatus)> SeedLifecycleAsync(string? role, CancellationToken cancellationToken = default)
    {
        _workflowIntegrityService.EnsureRoleAllowed("SeedLifecycle", role);

        var seeded = await _workflowTransitionService.SeedLifecycleAsync(cancellationToken);

        _workflowAuditStore.Add(new WorkflowAuditRecord(
            Guid.NewGuid(),
            "Seed",
            seeded.CaseId,
            "SeedLifecycle",
            null,
            seeded.CaseStatus,
            NormalizeRole(role),
            true,
            "Seeded lifecycle scenario.",
            DateTime.UtcNow));

        return seeded;
    }

    private async Task<(Guid Id, string Status)> RunCaseActionAsync(
        string actionName,
        Guid caseId,
        string nextStatus,
        string? role,
        Func<Guid, CancellationToken, Task<(Guid Id, string Status)>> operation,
        CancellationToken cancellationToken)
    {
        var entity = await _dbContext.Cases.FirstOrDefaultAsync(x => x.Id == caseId, cancellationToken)
            ?? throw new InvalidOperationException($"Case '{caseId}' was not found.");

        var previous = entity.Status.ToString();
        _workflowIntegrityService.EnsureRoleAllowed(actionName, role);
        _workflowIntegrityService.EnsureTransitionAllowed("Case", previous, nextStatus);

        try
        {
            var result = await operation(caseId, cancellationToken);
            _workflowAuditStore.Add(new WorkflowAuditRecord(Guid.NewGuid(), "Case", caseId, actionName, previous, result.Status, NormalizeRole(role), true, "Transition succeeded.", DateTime.UtcNow));
            return result;
        }
        catch (Exception ex)
        {
            _workflowAuditStore.Add(new WorkflowAuditRecord(Guid.NewGuid(), "Case", caseId, actionName, previous, nextStatus, NormalizeRole(role), false, ex.Message, DateTime.UtcNow));
            throw;
        }
    }

    private async Task<(Guid Id, string Status)> RunClaimActionAsync(
        string actionName,
        Guid claimId,
        string nextStatus,
        string? role,
        Func<Guid, CancellationToken, Task<(Guid Id, string Status)>> operation,
        CancellationToken cancellationToken)
    {
        var entity = await _dbContext.Claims.FirstOrDefaultAsync(x => x.Id == claimId, cancellationToken)
            ?? throw new InvalidOperationException($"Claim '{claimId}' was not found.");

        var previous = entity.Status.ToString();
        _workflowIntegrityService.EnsureRoleAllowed(actionName, role);
        _workflowIntegrityService.EnsureTransitionAllowed("Claim", previous, nextStatus);

        try
        {
            var result = await operation(claimId, cancellationToken);
            _workflowAuditStore.Add(new WorkflowAuditRecord(Guid.NewGuid(), "Claim", claimId, actionName, previous, result.Status, NormalizeRole(role), true, "Transition succeeded.", DateTime.UtcNow));
            return result;
        }
        catch (Exception ex)
        {
            _workflowAuditStore.Add(new WorkflowAuditRecord(Guid.NewGuid(), "Claim", claimId, actionName, previous, nextStatus, NormalizeRole(role), false, ex.Message, DateTime.UtcNow));
            throw;
        }
    }

    private async Task<(Guid Id, string Status)> RunContradictionActionAsync(
        string actionName,
        Guid contradictionId,
        string nextStatus,
        string? role,
        Func<Guid, CancellationToken, Task<(Guid Id, string Status)>> operation,
        CancellationToken cancellationToken)
    {
        var entity = await _dbContext.Contradictions.FirstOrDefaultAsync(x => x.Id == contradictionId, cancellationToken)
            ?? throw new InvalidOperationException($"Contradiction '{contradictionId}' was not found.");

        var previous = entity.Status.ToString();
        _workflowIntegrityService.EnsureRoleAllowed(actionName, role);
        _workflowIntegrityService.EnsureTransitionAllowed("Contradiction", previous, nextStatus);

        try
        {
            var result = await operation(contradictionId, cancellationToken);
            _workflowAuditStore.Add(new WorkflowAuditRecord(Guid.NewGuid(), "Contradiction", contradictionId, actionName, previous, result.Status, NormalizeRole(role), true, "Transition succeeded.", DateTime.UtcNow));
            return result;
        }
        catch (Exception ex)
        {
            _workflowAuditStore.Add(new WorkflowAuditRecord(Guid.NewGuid(), "Contradiction", contradictionId, actionName, previous, nextStatus, NormalizeRole(role), false, ex.Message, DateTime.UtcNow));
            throw;
        }
    }

    private async Task<(Guid Id, string Status)> RunReviewActionAsync(
        string actionName,
        Guid reviewId,
        string nextStatus,
        string? role,
        Func<Guid, CancellationToken, Task<(Guid Id, string Status)>> operation,
        CancellationToken cancellationToken)
    {
        var entity = await _dbContext.Reviews.FirstOrDefaultAsync(x => x.Id == reviewId, cancellationToken)
            ?? throw new InvalidOperationException($"Review '{reviewId}' was not found.");

        var previous = entity.Status.ToString();
        _workflowIntegrityService.EnsureRoleAllowed(actionName, role);
        _workflowIntegrityService.EnsureTransitionAllowed("Review", previous, nextStatus);

        try
        {
            var result = await operation(reviewId, cancellationToken);
            _workflowAuditStore.Add(new WorkflowAuditRecord(Guid.NewGuid(), "Review", reviewId, actionName, previous, result.Status, NormalizeRole(role), true, "Transition succeeded.", DateTime.UtcNow));
            return result;
        }
        catch (Exception ex)
        {
            _workflowAuditStore.Add(new WorkflowAuditRecord(Guid.NewGuid(), "Review", reviewId, actionName, previous, nextStatus, NormalizeRole(role), false, ex.Message, DateTime.UtcNow));
            throw;
        }
    }

    private static string NormalizeRole(string? role)
    {
        return string.IsNullOrWhiteSpace(role) ? "anonymous" : role.Trim().ToLowerInvariant();
    }
}
'@

# DI registration
$diPath = Join-Path $infra "Extensions\ServiceCollectionExtensions.cs"
if (Test-Path $diPath) {
    $diContent = Get-Content $diPath -Raw

    if ($diContent -notmatch 'WorkflowIntegrityService') {
        $diContent = $diContent -replace '(services\.AddScoped<WorkflowTransitionService>\(\);)', '$1
        services.AddSingleton<WorkflowAuditStore>();
        services.AddSingleton<WorkflowIntegrityService>();
        services.AddScoped<WorkflowOrchestratorService>();'
        Write-File -Path $diPath -Content $diContent
    }
}

# Controllers rewritten to use orchestrator
Write-File -Path (Join-Path $apiProj "Controllers\ActionsController.cs") -Content @'
using Microsoft.AspNetCore.Mvc;
using VeritasAtlas.Api.Contracts.Workflow;
using VeritasAtlas.Infrastructure.Services;

namespace VeritasAtlas.Api.Controllers;

[ApiController]
[Route("api/v1/actions")]
public class ActionsController : ControllerBase
{
    private readonly WorkflowOrchestratorService _workflowOrchestratorService;

    public ActionsController(WorkflowOrchestratorService workflowOrchestratorService)
    {
        _workflowOrchestratorService = workflowOrchestratorService;
    }

    [HttpPost("cases/{caseId}/submit")]
    public async Task<ActionResult<WorkflowTransitionResponse>> SubmitCase(Guid caseId, CancellationToken cancellationToken)
    {
        return await ExecuteTransition(
            "Case",
            caseId,
            () => _workflowOrchestratorService.SubmitCaseAsync(caseId, Request.Headers["X-Role"], cancellationToken),
            "Case submitted.");
    }

    [HttpPost("cases/{caseId}/approve")]
    public async Task<ActionResult<WorkflowTransitionResponse>> ApproveCase(Guid caseId, CancellationToken cancellationToken)
    {
        return await ExecuteTransition(
            "Case",
            caseId,
            () => _workflowOrchestratorService.ApproveCaseAsync(caseId, Request.Headers["X-Role"], cancellationToken),
            "Case approved.");
    }

    [HttpPost("cases/{caseId}/reject")]
    public async Task<ActionResult<WorkflowTransitionResponse>> RejectCase(Guid caseId, CancellationToken cancellationToken)
    {
        return await ExecuteTransition(
            "Case",
            caseId,
            () => _workflowOrchestratorService.RejectCaseAsync(caseId, Request.Headers["X-Role"], cancellationToken),
            "Case rejected.");
    }

    [HttpPost("contradictions/{id}/resolve")]
    public async Task<ActionResult<WorkflowTransitionResponse>> ResolveContradiction(Guid id, CancellationToken cancellationToken)
    {
        return await ExecuteTransition(
            "Contradiction",
            id,
            () => _workflowOrchestratorService.ResolveContradictionAsync(id, Request.Headers["X-Role"], cancellationToken),
            "Contradiction resolved.");
    }

    [HttpPost("reviews/{id}/complete")]
    public async Task<ActionResult<WorkflowTransitionResponse>> CompleteReview(Guid id, CancellationToken cancellationToken)
    {
        return await ExecuteTransition(
            "Review",
            id,
            () => _workflowOrchestratorService.CompleteReviewAsync(id, Request.Headers["X-Role"], cancellationToken),
            "Review completed.");
    }

    [HttpPost("seed/lifecycle")]
    public async Task<ActionResult<WorkflowSeedResponse>> SeedLifecycle(CancellationToken cancellationToken)
    {
        try
        {
            var result = await _workflowOrchestratorService.SeedLifecycleAsync(Request.Headers["X-Role"], cancellationToken);

            return Ok(new WorkflowSeedResponse(
                result.CaseId,
                result.ClaimAId,
                result.ClaimBId,
                result.ContradictionId,
                result.CaseStatus,
                result.ContradictionStatus,
                DateTime.UtcNow));
        }
        catch (UnauthorizedAccessException ex)
        {
            return StatusCode(StatusCodes.Status403Forbidden, ex.Message);
        }
        catch (InvalidOperationException ex)
        {
            return BadRequest(ex.Message);
        }
    }

    private async Task<ActionResult<WorkflowTransitionResponse>> ExecuteTransition(
        string entityType,
        Guid entityId,
        Func<Task<(Guid Id, string Status)>> action,
        string message)
    {
        try
        {
            var result = await action();
            return Ok(new WorkflowTransitionResponse(entityType, result.Id, result.Status, DateTime.UtcNow, message));
        }
        catch (UnauthorizedAccessException ex)
        {
            return StatusCode(StatusCodes.Status403Forbidden, ex.Message);
        }
        catch (InvalidOperationException ex)
        {
            return BadRequest(new WorkflowValidationFailureResponse(entityType, entityId, message, ex.Message, DateTime.UtcNow));
        }
    }
}
'@

Write-File -Path (Join-Path $apiProj "Controllers\ReviewWorkflowController.cs") -Content @'
using Microsoft.AspNetCore.Mvc;
using VeritasAtlas.Api.Contracts.Workflow;
using VeritasAtlas.Infrastructure.Services;

namespace VeritasAtlas.Api.Controllers;

[ApiController]
[Route("api/v1/review-workflow")]
public class ReviewWorkflowController : ControllerBase
{
    private readonly WorkflowOrchestratorService _workflowOrchestratorService;

    public ReviewWorkflowController(WorkflowOrchestratorService workflowOrchestratorService)
    {
        _workflowOrchestratorService = workflowOrchestratorService;
    }

    [HttpPost("claims/{claimId}/send-to-review")]
    public async Task<ActionResult<WorkflowTransitionResponse>> SendClaimToReview(Guid claimId, CancellationToken cancellationToken)
    {
        return await ExecuteTransition(
            "Claim",
            claimId,
            () => _workflowOrchestratorService.SendClaimToReviewAsync(claimId, Request.Headers["X-Role"], cancellationToken),
            "Claim sent to review.");
    }

    [HttpPost("claims/{claimId}/return-for-edit")]
    public async Task<ActionResult<WorkflowTransitionResponse>> ReturnClaimForEdit(Guid claimId, CancellationToken cancellationToken)
    {
        return await ExecuteTransition(
            "Claim",
            claimId,
            () => _workflowOrchestratorService.ReturnClaimForEditAsync(claimId, Request.Headers["X-Role"], cancellationToken),
            "Claim returned for edit.");
    }

    [HttpPost("contradictions/{id}/escalate")]
    public async Task<ActionResult<WorkflowTransitionResponse>> EscalateContradiction(Guid id, CancellationToken cancellationToken)
    {
        return await ExecuteTransition(
            "Contradiction",
            id,
            () => _workflowOrchestratorService.EscalateContradictionAsync(id, Request.Headers["X-Role"], cancellationToken),
            "Contradiction escalated.");
    }

    [HttpPost("reviews/{id}/reopen")]
    public async Task<ActionResult<WorkflowTransitionResponse>> ReopenReview(Guid id, CancellationToken cancellationToken)
    {
        return await ExecuteTransition(
            "Review",
            id,
            () => _workflowOrchestratorService.ReopenReviewAsync(id, Request.Headers["X-Role"], cancellationToken),
            "Review reopened.");
    }

    private async Task<ActionResult<WorkflowTransitionResponse>> ExecuteTransition(
        string entityType,
        Guid entityId,
        Func<Task<(Guid Id, string Status)>> action,
        string message)
    {
        try
        {
            var result = await action();
            return Ok(new WorkflowTransitionResponse(entityType, result.Id, result.Status, DateTime.UtcNow, message));
        }
        catch (UnauthorizedAccessException ex)
        {
            return StatusCode(StatusCodes.Status403Forbidden, ex.Message);
        }
        catch (InvalidOperationException ex)
        {
            return BadRequest(new WorkflowValidationFailureResponse(entityType, entityId, message, ex.Message, DateTime.UtcNow));
        }
    }
}
'@

Write-File -Path (Join-Path $apiProj "Controllers\PublicationWorkflowController.cs") -Content @'
using Microsoft.AspNetCore.Mvc;
using VeritasAtlas.Api.Contracts.Workflow;
using VeritasAtlas.Infrastructure.Services;

namespace VeritasAtlas.Api.Controllers;

[ApiController]
[Route("api/v1/publication-workflow")]
public class PublicationWorkflowController : ControllerBase
{
    private readonly WorkflowOrchestratorService _workflowOrchestratorService;

    public PublicationWorkflowController(WorkflowOrchestratorService workflowOrchestratorService)
    {
        _workflowOrchestratorService = workflowOrchestratorService;
    }

    [HttpPost("cases/{caseId}/prepare")]
    public async Task<ActionResult<WorkflowTransitionResponse>> PreparePublication(Guid caseId, CancellationToken cancellationToken)
    {
        return await ExecuteTransition(
            "Case",
            caseId,
            () => _workflowOrchestratorService.PreparePublicationAsync(caseId, Request.Headers["X-Role"], cancellationToken),
            "Case prepared for publication.");
    }

    [HttpPost("cases/{caseId}/publish")]
    public async Task<ActionResult<WorkflowTransitionResponse>> PublishCase(Guid caseId, CancellationToken cancellationToken)
    {
        return await ExecuteTransition(
            "Case",
            caseId,
            () => _workflowOrchestratorService.PublishCaseAsync(caseId, Request.Headers["X-Role"], cancellationToken),
            "Case published.");
    }

    [HttpPost("cases/{caseId}/hold")]
    public async Task<ActionResult<WorkflowTransitionResponse>> HoldCase(Guid caseId, CancellationToken cancellationToken)
    {
        return await ExecuteTransition(
            "Case",
            caseId,
            () => _workflowOrchestratorService.HoldCaseAsync(caseId, Request.Headers["X-Role"], cancellationToken),
            "Case put on hold.");
    }

    private async Task<ActionResult<WorkflowTransitionResponse>> ExecuteTransition(
        string entityType,
        Guid entityId,
        Func<Task<(Guid Id, string Status)>> action,
        string message)
    {
        try
        {
            var result = await action();
            return Ok(new WorkflowTransitionResponse(entityType, result.Id, result.Status, DateTime.UtcNow, message));
        }
        catch (UnauthorizedAccessException ex)
        {
            return StatusCode(StatusCodes.Status403Forbidden, ex.Message);
        }
        catch (InvalidOperationException ex)
        {
            return BadRequest(new WorkflowValidationFailureResponse(entityType, entityId, message, ex.Message, DateTime.UtcNow));
        }
    }
}
'@

Write-File -Path (Join-Path $apiProj "Controllers\WorkflowAuditController.cs") -Content @'
using Microsoft.AspNetCore.Mvc;
using VeritasAtlas.Api.Contracts.Workflow;
using VeritasAtlas.Infrastructure.Services;

namespace VeritasAtlas.Api.Controllers;

[ApiController]
[Route("api/v1/workflow-audit")]
public class WorkflowAuditController : ControllerBase
{
    private readonly WorkflowAuditStore _workflowAuditStore;
    private readonly WorkflowIntegrityService _workflowIntegrityService;

    public WorkflowAuditController(
        WorkflowAuditStore workflowAuditStore,
        WorkflowIntegrityService workflowIntegrityService)
    {
        _workflowAuditStore = workflowAuditStore;
        _workflowIntegrityService = workflowIntegrityService;
    }

    [HttpGet("entries")]
    public ActionResult<IReadOnlyList<WorkflowAuditEntryResponse>> GetEntries()
    {
        var entries = _workflowAuditStore.GetAll()
            .Select(x => new WorkflowAuditEntryResponse(
                x.Id,
                x.EntityType,
                x.EntityId,
                x.ActionName,
                x.PreviousStatus,
                x.NextStatus,
                x.Role,
                x.Success,
                x.Message,
                x.TimestampUtc))
            .ToList();

        return Ok(entries);
    }

    [HttpPost("clear")]
    public IActionResult Clear()
    {
        _workflowAuditStore.Clear();
        return Ok(new { Cleared = true, TimestampUtc = DateTime.UtcNow });
    }

    [HttpGet("rules")]
    public IActionResult GetRules()
    {
        return Ok(new
        {
            Transitions = _workflowIntegrityService.GetRules(),
            Roles = _workflowIntegrityService.GetRolePolicies(),
            TimestampUtc = DateTime.UtcNow
        });
    }
}
'@

# Frontend role context and reliability
Write-File -Path (Join-Path $web "api\workflowRoleContext.ts") -Content @'
const ROLE_KEY = "veritas-workflow-role";

export function getWorkflowRole(): string {
  if (typeof window === "undefined") {
    return "operator";
  }

  return window.localStorage.getItem(ROLE_KEY) ?? "operator";
}

export function setWorkflowRole(role: string) {
  if (typeof window === "undefined") {
    return;
  }

  window.localStorage.setItem(ROLE_KEY, role);
}

export function getWorkflowRoleHeaders(): HeadersInit {
  return {
    "X-Role": getWorkflowRole(),
  };
}
'@

Write-File -Path (Join-Path $web "api\actions.ts") -Content @'
import { getWorkflowRoleHeaders } from "./workflowRoleContext";

export type ActionResponse = {
  caseId?: string;
  contradictionId?: string;
  reviewId?: string;
  status: string;
  timestampUtc?: string;
  timestamp?: string;
};

async function postAction(url: string): Promise<ActionResponse> {
  const response = await fetch(url, {
    method: "POST",
    headers: getWorkflowRoleHeaders(),
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

Write-File -Path (Join-Path $web "api\workflowActions.ts") -Content @'
import { getWorkflowRoleHeaders } from "./workflowRoleContext";

export type WorkflowActionResponse = {
  claimId?: string;
  contradictionId?: string;
  reviewId?: string;
  caseId?: string;
  status: string;
  timestampUtc?: string;
  timestamp?: string;
};

async function postAction(url: string): Promise<WorkflowActionResponse> {
  const response = await fetch(url, {
    method: "POST",
    headers: getWorkflowRoleHeaders(),
  });

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

Write-File -Path (Join-Path $web "api\workflowSeed.ts") -Content @'
import { getWorkflowRoleHeaders } from "./workflowRoleContext";

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
  const response = await fetch("/api/v1/actions/seed/lifecycle", {
    method: "POST",
    headers: getWorkflowRoleHeaders(),
  });

  if (!response.ok) {
    const text = await response.text();
    throw new Error(text || `HTTP ${response.status}`);
  }

  return response.json() as Promise<WorkflowSeedResponse>;
}
'@

Write-File -Path (Join-Path $web "api\workflowAudit.ts") -Content @'
export type WorkflowAuditEntry = {
  id: string;
  entityType: string;
  entityId: string;
  actionName: string;
  previousStatus?: string | null;
  nextStatus: string;
  role: string;
  success: boolean;
  message: string;
  timestampUtc: string;
};

export async function getWorkflowAuditEntries(): Promise<WorkflowAuditEntry[]> {
  const response = await fetch("/api/v1/workflow-audit/entries");

  if (!response.ok) {
    const text = await response.text();
    throw new Error(text || `HTTP ${response.status}`);
  }

  return response.json() as Promise<WorkflowAuditEntry[]>;
}

export async function clearWorkflowAudit(): Promise<void> {
  const response = await fetch("/api/v1/workflow-audit/clear", { method: "POST" });

  if (!response.ok) {
    const text = await response.text();
    throw new Error(text || `HTTP ${response.status}`);
  }
}
'@

Write-File -Path (Join-Path $web "hooks\useWorkflowAudit.ts") -Content @'
import { useMutation, useQuery, useQueryClient } from "@tanstack/react-query";
import { clearWorkflowAudit, getWorkflowAuditEntries } from "../api/workflowAudit";

export function useWorkflowAuditEntries() {
  return useQuery({
    queryKey: ["workflow-audit-entries"],
    queryFn: () => getWorkflowAuditEntries(),
  });
}

export function useClearWorkflowAudit() {
  const queryClient = useQueryClient();

  return useMutation({
    mutationFn: () => clearWorkflowAudit(),
    onSuccess: () => {
      queryClient.invalidateQueries({ queryKey: ["workflow-audit-entries"] });
    },
  });
}
'@

Write-File -Path (Join-Path $web "api\workflowValidation.ts") -Content @'
export type WorkflowRulesPayload = {
  transitions: Record<string, Record<string, string[]>>;
  roles: Record<string, string[]>;
  timestampUtc: string;
};

export async function getWorkflowRules(): Promise<WorkflowRulesPayload> {
  const response = await fetch("/api/v1/workflow-audit/rules");

  if (!response.ok) {
    const text = await response.text();
    throw new Error(text || `HTTP ${response.status}`);
  }

  return response.json() as Promise<WorkflowRulesPayload>;
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

Write-File -Path (Join-Path $web "components\RoleSelectorPanel.tsx") -Content @'
import { useEffect, useState } from "react";
import { getWorkflowRole, setWorkflowRole } from "../api/workflowRoleContext";

const roles = ["operator", "reviewer", "publisher", "admin"];

export function RoleSelectorPanel() {
  const [role, setRole] = useState("operator");

  useEffect(() => {
    setRole(getWorkflowRole());
  }, []);

  const onRoleChange = (value: string) => {
    setRole(value);
    setWorkflowRole(value);
  };

  return (
    <div style={panelStyle}>
      <strong>Workflow Role</strong>
      <select value={role} onChange={(e) => onRoleChange(e.target.value)} style={selectStyle}>
        {roles.map((item) => (
          <option key={item} value={item}>{item}</option>
        ))}
      </select>
    </div>
  );
}

const panelStyle: React.CSSProperties = {
  border: "1px solid #ddd",
  borderRadius: 12,
  padding: 12,
  display: "flex",
  gap: 12,
  alignItems: "center",
};

const selectStyle: React.CSSProperties = {
  border: "1px solid #ccc",
  borderRadius: 8,
  padding: "8px 10px",
  font: "inherit",
};
'@

Write-File -Path (Join-Path $web "components\ActionButtonsPanel.tsx") -Content @'
type ActionButtonItem = {
  label: string;
  onClick: () => void;
  disabled?: boolean;
};

export function ActionButtonsPanel({
  title,
  items,
  message,
  error,
  isBusy,
}: {
  title: string;
  items: ActionButtonItem[];
  message?: string;
  error?: string;
  isBusy?: boolean;
}) {
  return (
    <div style={panelStyle}>
      <h3 style={{ marginTop: 0 }}>{title}</h3>
      <div style={{ display: "flex", gap: 12, flexWrap: "wrap" }}>
        {items.map((item) => (
          <button
            key={item.label}
            onClick={item.onClick}
            disabled={item.disabled || isBusy}
            style={buttonStyle}
          >
            {item.label}
          </button>
        ))}
      </div>
      {isBusy && <p style={{ marginTop: 12, marginBottom: 0 }}>Working...</p>}
      {message && <p style={{ marginTop: 12, marginBottom: 0 }}>{message}</p>}
      {error && <p style={{ marginTop: 12, marginBottom: 0, color: "crimson" }}>{error}</p>}
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

# Rewrite workflow-oriented pages with better reliability
Write-File -Path (Join-Path $web "pages\OperationalActionsPage.tsx") -Content @'
import { useState } from "react";
import { ActionButtonsPanel } from "../components/ActionButtonsPanel";
import { RoleSelectorPanel } from "../components/RoleSelectorPanel";
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

  const caseMessage = submitCaseAction.data?.status || approveCaseAction.data?.status || rejectCaseAction.data?.status;
  const caseError =
    (submitCaseAction.error as Error | null)?.message ||
    (approveCaseAction.error as Error | null)?.message ||
    (rejectCaseAction.error as Error | null)?.message;

  const reviewMessage = resolveContradictionAction.data?.status || completeReviewAction.data?.status;
  const reviewError =
    (resolveContradictionAction.error as Error | null)?.message ||
    (completeReviewAction.error as Error | null)?.message;

  return (
    <div style={{ fontFamily: "Arial, sans-serif", padding: 24 }}>
      <h1 style={{ marginTop: 0 }}>Operational Actions</h1>
      <p style={{ color: "#555" }}>
        Manual action surface for submitting, approving, rejecting, resolving, and completing operational items.
      </p>

      <RoleSelectorPanel />

      <div style={{ ...panelStyle, marginTop: 16 }}>
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
          message={caseMessage}
          error={caseError}
          isBusy={submitCaseAction.isPending || approveCaseAction.isPending || rejectCaseAction.isPending}
        />

        <ActionButtonsPanel
          title="Resolution / Review Actions"
          items={[
            { label: "Resolve Contradiction", onClick: () => resolveContradictionAction.mutate(contradictionId), disabled: !contradictionId },
            { label: "Complete Review", onClick: () => completeReviewAction.mutate(reviewId), disabled: !reviewId },
          ]}
          message={reviewMessage}
          error={reviewError}
          isBusy={resolveContradictionAction.isPending || completeReviewAction.isPending}
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

Write-File -Path (Join-Path $web "pages\WorkflowConsolePage.tsx") -Content @'
import { useState } from "react";
import { WorkflowActionPanel } from "../components/WorkflowActionPanel";
import { RoleSelectorPanel } from "../components/RoleSelectorPanel";
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

  const reviewMessage = sendClaim.data?.status || returnClaim.data?.status || escalate.data?.status || reopen.data?.status;
  const reviewError =
    (sendClaim.error as Error | null)?.message ||
    (returnClaim.error as Error | null)?.message ||
    (escalate.error as Error | null)?.message ||
    (reopen.error as Error | null)?.message;

  const publicationMessage = prepare.data?.status || publish.data?.status || hold.data?.status;
  const publicationError =
    (prepare.error as Error | null)?.message ||
    (publish.error as Error | null)?.message ||
    (hold.error as Error | null)?.message;

  return (
    <div style={{ fontFamily: "Arial, sans-serif", padding: 24 }}>
      <h1 style={{ marginTop: 0 }}>Workflow Console</h1>
      <p style={{ color: "#555" }}>
        Console for review routing, contradiction escalation, and publication workflow actions.
      </p>

      <RoleSelectorPanel />

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
          message={reviewMessage}
          error={reviewError}
          isBusy={sendClaim.isPending || returnClaim.isPending || escalate.isPending || reopen.isPending}
        />

        <WorkflowActionPanel
          title="Publication Workflow"
          buttons={[
            { label: "Prepare Publication", onClick: () => prepare.mutate(caseId), disabled: !caseId },
            { label: "Publish Case", onClick: () => publish.mutate(caseId), disabled: !caseId },
            { label: "Hold Case", onClick: () => hold.mutate(caseId), disabled: !caseId },
          ]}
          message={publicationMessage}
          error={publicationError}
          isBusy={prepare.isPending || publish.isPending || hold.isPending}
        />
      </div>
    </div>
  );
}

const inputGridStyle: React.CSSProperties = {
  display: "grid",
  gridTemplateColumns: "1fr 1fr",
  gap: 12,
  marginTop: 16,
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

Write-File -Path (Join-Path $web "components\WorkflowActionPanel.tsx") -Content @'
type WorkflowActionButton = {
  label: string;
  onClick: () => void;
  disabled?: boolean;
};

export function WorkflowActionPanel({
  title,
  buttons,
  message,
  error,
  isBusy,
}: {
  title: string;
  buttons: WorkflowActionButton[];
  message?: string;
  error?: string;
  isBusy?: boolean;
}) {
  return (
    <div style={panelStyle}>
      <h3 style={{ marginTop: 0 }}>{title}</h3>
      <div style={{ display: "flex", gap: 12, flexWrap: "wrap" }}>
        {buttons.map((button) => (
          <button
            key={button.label}
            onClick={button.onClick}
            disabled={button.disabled || isBusy}
            style={buttonStyle}
          >
            {button.label}
          </button>
        ))}
      </div>
      {isBusy && <p style={{ marginTop: 12, marginBottom: 0 }}>Working...</p>}
      {message && <p style={{ marginTop: 12, marginBottom: 0 }}>{message}</p>}
      {error && <p style={{ marginTop: 12, marginBottom: 0, color: "crimson" }}>{error}</p>}
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

Write-File -Path (Join-Path $web "pages\LifecycleSeedPage.tsx") -Content @'
import { RoleSelectorPanel } from "../components/RoleSelectorPanel";
import { SeedScenarioPanel } from "../components/SeedScenarioPanel";
import { useWorkflowSeed } from "../hooks/useWorkflowSeed";

export function LifecycleSeedPage() {
  const seedMutation = useWorkflowSeed();

  const message = seedMutation.data
    ? `Case ${seedMutation.data.caseId} seeded with contradiction ${seedMutation.data.contradictionId}.`
    : undefined;

  const error = (seedMutation.error as Error | null)?.message;

  return (
    <div style={{ fontFamily: "Arial, sans-serif", padding: 24 }}>
      <h1 style={{ marginTop: 0 }}>Lifecycle Seed</h1>
      <p style={{ color: "#555" }}>
        Seed a minimal end-to-end workflow scenario for validation and UI interaction.
      </p>

      <RoleSelectorPanel />

      <div style={{ marginTop: 16 }}>
        <SeedScenarioPanel
          onSeed={() => seedMutation.mutate()}
          isPending={seedMutation.isPending}
          message={message || error}
        />
      </div>
    </div>
  );
}
'@

Write-File -Path (Join-Path $web "pages\WorkflowAuditPage.tsx") -Content @'
import { useClearWorkflowAudit, useWorkflowAuditEntries } from "../hooks/useWorkflowAudit";

export function WorkflowAuditPage() {
  const entriesQuery = useWorkflowAuditEntries();
  const clearMutation = useClearWorkflowAudit();

  if (entriesQuery.isLoading) {
    return <div style={{ fontFamily: "Arial, sans-serif", padding: 24 }}>Loading workflow audit...</div>;
  }

  if (entriesQuery.isError) {
    return <div style={{ fontFamily: "Arial, sans-serif", padding: 24, color: "crimson" }}>Failed to load workflow audit.</div>;
  }

  const entries = entriesQuery.data ?? [];

  return (
    <div style={{ fontFamily: "Arial, sans-serif", padding: 24 }}>
      <h1 style={{ marginTop: 0 }}>Workflow Audit</h1>
      <button onClick={() => clearMutation.mutate()} style={buttonStyle}>Clear Audit</button>

      <div style={{ ...panelStyle, marginTop: 16 }}>
        {entries.length === 0 && <p>No audit entries.</p>}
        {entries.length > 0 && (
          <ul style={{ marginBottom: 0 }}>
            {entries.map((entry) => (
              <li key={entry.id}>
                [{entry.timestampUtc}] {entry.entityType} {entry.entityId} - {entry.actionName} - {entry.previousStatus ?? "N/A"} → {entry.nextStatus} - {entry.role} - {entry.success ? "OK" : "FAIL"}
              </li>
            ))}
          </ul>
        )}
      </div>
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

Write-File -Path (Join-Path $web "pages\WorkflowValidationPage.tsx") -Content @'
import { useWorkflowValidation } from "../hooks/useWorkflowValidation";

export function WorkflowValidationPage() {
  const query = useWorkflowValidation();

  if (query.isLoading) {
    return <div style={{ fontFamily: "Arial, sans-serif", padding: 24 }}>Loading workflow validation...</div>;
  }

  if (query.isError) {
    return <div style={{ fontFamily: "Arial, sans-serif", padding: 24, color: "crimson" }}>Failed to load workflow validation rules.</div>;
  }

  const payload = query.data;

  if (!payload) {
    return <div style={{ fontFamily: "Arial, sans-serif", padding: 24 }}>No workflow rules available.</div>;
  }

  return (
    <div style={{ fontFamily: "Arial, sans-serif", padding: 24 }}>
      <h1 style={{ marginTop: 0 }}>Workflow Validation</h1>
      <p style={{ color: "#555" }}>
        Transition rules and role policies for the workflow layer.
      </p>

      <div style={gridStyle}>
        <div style={panelStyle}>
          <h3 style={{ marginTop: 0 }}>Transition Rules</h3>
          <ul style={{ marginBottom: 0 }}>
            {Object.entries(payload.transitions).map(([entityType, rules]) => (
              <li key={entityType}>
                <strong>{entityType}</strong>
                <ul>
                  {Object.entries(rules).map(([from, targets]) => (
                    <li key={from}>{from} → {targets.join(", ") || "∅"}</li>
                  ))}
                </ul>
              </li>
            ))}
          </ul>
        </div>

        <div style={panelStyle}>
          <h3 style={{ marginTop: 0 }}>Role Policies</h3>
          <ul style={{ marginBottom: 0 }}>
            {Object.entries(payload.roles).map(([actionName, roles]) => (
              <li key={actionName}>
                <strong>{actionName}</strong>: {roles.join(", ")}
              </li>
            ))}
          </ul>
        </div>
      </div>
    </div>
  );
}

const gridStyle: React.CSSProperties = {
  display: "grid",
  gridTemplateColumns: "1fr 1fr",
  gap: 16,
};

const panelStyle: React.CSSProperties = {
  border: "1px solid #ddd",
  borderRadius: 14,
  padding: 16,
};
'@

Write-File -Path (Join-Path $web "pages\Phase9IntegrityCenterPage.tsx") -Content @'
import { Link } from "react-router-dom";

export function Phase9IntegrityCenterPage() {
  return (
    <div style={{ fontFamily: "Arial, sans-serif", padding: 24 }}>
      <h1 style={{ marginTop: 0 }}>Phase 9 Integrity Center</h1>
      <p style={{ color: "#555" }}>
        Central surface for workflow integrity, validation, diagnostics, role testing, and seeded lifecycle execution.
      </p>

      <div style={gridStyle}>
        <Link to="/workflow-console" style={cardStyle}>Workflow Console</Link>
        <Link to="/workflow-diagnostics" style={cardStyle}>Workflow Diagnostics</Link>
        <Link to="/workflow-audit" style={cardStyle}>Workflow Audit</Link>
        <Link to="/workflow-validation" style={cardStyle}>Workflow Validation</Link>
        <Link to="/lifecycle-seed" style={cardStyle}>Lifecycle Seed</Link>
        <Link to="/seeded-lifecycle-runner" style={cardStyle}>Seeded Lifecycle Runner</Link>
        <Link to="/mutation-playground" style={cardStyle}>Mutation Playground</Link>
        <Link to="/integration-test-center" style={cardStyle}>Integration Test Center</Link>
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

# Routes and nav
$main = Join-Path $web "main.tsx"
$mainContent = Get-Content $main -Raw

$mainContent = Ensure-ImportLine -Content $mainContent -Anchor 'import { DashboardPage } from "./pages/DashboardPage";' -ImportLine 'import { WorkflowAuditPage } from "./pages/WorkflowAuditPage";'
$mainContent = Ensure-ImportLine -Content $mainContent -Anchor 'import { WorkflowAuditPage } from "./pages/WorkflowAuditPage";' -ImportLine 'import { Phase9IntegrityCenterPage } from "./pages/Phase9IntegrityCenterPage";'

$mainContent = Ensure-RouteBlock -Content $mainContent -AnchorRoute '{ path: "/dashboard", element: <DashboardPage /> },' -RouteBlock '{ path: "/workflow-audit", element: <WorkflowAuditPage /> },
  { path: "/phase-9-integrity-center", element: <Phase9IntegrityCenterPage /> },' -PresencePattern 'path: "/workflow-audit"'

$mainContent = Ensure-NavBlock -Content $mainContent -Anchor '<Link to="/workflow-diagnostics">Workflow Diagnostics</Link>' -NavBlock '<Link to="/workflow-audit">Workflow Audit</Link>
          <Link to="/phase-9-integrity-center">Phase 9 Integrity</Link>' -PresencePattern 'to="/workflow-audit"'

Write-File -Path $main -Content $mainContent

# Comprehensive automatic tests
Write-File -Path (Join-Path $tools "tests\Run-Phase9-Verification.ps1") -Content @'
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
    $apiProcess = Start-Process "dotnet" -ArgumentList "run" -NoNewWindow -Wait
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
'@

Write-File -Path (Join-Path $diag "phase-9-summary.md") -Content @'
# Phase 9 Summary

## Included
- workflow integrity validation service
- workflow audit store and API
- role-based action enforcement
- transition validation before mutations
- better frontend action reliability and role selection
- workflow audit and phase 9 integrity pages
- comprehensive automatic verification runner

## Automatic tests
The script will build the solution and create a dedicated verification runner at:
tools/tests/Run-Phase9-Verification.ps1

It is intended to execute:
- lifecycle seeding
- role enforcement checks
- transition correctness checks
- contradiction escalation/resolution checks
- audit verification
- rules verification
'@

Write-Host "Building..." -ForegroundColor Cyan
Build-All -RootDir $RootDir

Write-Host "Running comprehensive automatic Phase 9 tests..." -ForegroundColor Cyan
Invoke-Phase9Tests -RootDir $RootDir

Write-Host "Phase 9 bundle DONE" -ForegroundColor Green
