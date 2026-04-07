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

function Invoke-Phase10Tests {
    param(
        [Parameter(Mandatory = $true)][string]$RootDir
    )

    $runner = Join-Path $RootDir "tools\tests\Run-Phase10-Verification.ps1"
    if (-not (Test-Path $runner)) {
        throw "Phase 10 test runner not found: $runner"
    }

    Push-Location $RootDir
    try {
        & powershell -ExecutionPolicy Bypass -File $runner -RootDir $RootDir
        if ($LASTEXITCODE -ne 0) {
            throw "Phase 10 verification runner failed."
        }
    }
    finally {
        Pop-Location
    }
}

Write-Host "Checkpointing current code with git..." -ForegroundColor Cyan
Git-Checkpoint -Message ("checkpoint before phase 10 bundle - " + (Get-Date -Format "yyyy-MM-dd HH:mm:ss"))

Write-Host "Applying Phase 10 bundle - production-grade hardening, auth scaffolding, audit persistence, and verification..." -ForegroundColor Cyan

$api = Join-Path $RootDir "apps\api"
$infra = Join-Path $api "VeritasAtlas.Infrastructure"
$apiProj = Join-Path $api "VeritasAtlas.Api"
$web = Join-Path $RootDir "apps\web\veritas-atlas-web\src"
$tools = Join-Path $RootDir "tools"
$diag = Join-Path $RootDir "_diagnostics\phase-10"
Ensure-Dir -Path $diag

# =========================
# BACKEND: auth/role context
# =========================

Write-File -Path (Join-Path $apiProj "Infrastructure\WorkflowRequestContext.cs") -Content @'
namespace VeritasAtlas.Api.Infrastructure;

public sealed class WorkflowRequestContext
{
    public string GetRole(HttpRequest request)
    {
        var value = request.Headers["X-Role"].ToString();
        return string.IsNullOrWhiteSpace(value) ? "anonymous" : value.Trim().ToLowerInvariant();
    }
}
'@

Write-File -Path (Join-Path $apiProj "Infrastructure\WorkflowAuthorizationService.cs") -Content @'
namespace VeritasAtlas.Api.Infrastructure;

public sealed class WorkflowAuthorizationService
{
    public void RequireKnownRole(string role)
    {
        var allowed = new[] { "anonymous", "operator", "reviewer", "publisher", "admin" };

        if (!allowed.Contains(role, StringComparer.OrdinalIgnoreCase))
        {
            throw new UnauthorizedAccessException($"Unknown role '{role}'.");
        }
    }
}
'@

# =========================
# BACKEND: persistent audit json store
# =========================

Write-File -Path (Join-Path $infra "Services\PersistentWorkflowAuditService.cs") -Content @'
using System.Text.Json;

namespace VeritasAtlas.Infrastructure.Services;

public sealed class PersistentWorkflowAuditService
{
    private readonly string _auditPath;
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web) { WriteIndented = true };

    public PersistentWorkflowAuditService()
    {
        var root = AppContext.BaseDirectory;
        _auditPath = Path.Combine(root, "workflow-audit-store.json");
    }

    public async Task AppendAsync(WorkflowAuditRecord record, CancellationToken cancellationToken = default)
    {
        var records = await ReadAllAsync(cancellationToken);
        records.Add(record);

        await using var stream = File.Create(_auditPath);
        await JsonSerializer.SerializeAsync(stream, records, JsonOptions, cancellationToken);
    }

    public async Task<IReadOnlyList<WorkflowAuditRecord>> GetAllAsync(CancellationToken cancellationToken = default)
    {
        var records = await ReadAllAsync(cancellationToken);
        return records.OrderByDescending(x => x.TimestampUtc).ToList();
    }

    public async Task ClearAsync(CancellationToken cancellationToken = default)
    {
        await using var stream = File.Create(_auditPath);
        await JsonSerializer.SerializeAsync(stream, new List<WorkflowAuditRecord>(), JsonOptions, cancellationToken);
    }

    private async Task<List<WorkflowAuditRecord>> ReadAllAsync(CancellationToken cancellationToken)
    {
        if (!File.Exists(_auditPath))
        {
            return new List<WorkflowAuditRecord>();
        }

        await using var stream = File.OpenRead(_auditPath);
        var result = await JsonSerializer.DeserializeAsync<List<WorkflowAuditRecord>>(stream, JsonOptions, cancellationToken);
        return result ?? new List<WorkflowAuditRecord>();
    }
}
'@

# =========================
# BACKEND: error contract
# =========================

Write-File -Path (Join-Path $apiProj "Contracts\Workflow\WorkflowErrorContracts.cs") -Content @'
namespace VeritasAtlas.Api.Contracts.Workflow;

public sealed record WorkflowErrorResponse(
    string Code,
    string Message,
    string? Detail,
    string Path,
    DateTime TimestampUtc);
'@

# =========================
# BACKEND: harden audit store to persistent
# =========================

Write-File -Path (Join-Path $infra "Services\WorkflowAuditStore.cs") -Content @'
namespace VeritasAtlas.Infrastructure.Services;

public sealed class WorkflowAuditStore
{
    private readonly PersistentWorkflowAuditService _persistentWorkflowAuditService;

    public WorkflowAuditStore(PersistentWorkflowAuditService persistentWorkflowAuditService)
    {
        _persistentWorkflowAuditService = persistentWorkflowAuditService;
    }

    public async Task AddAsync(WorkflowAuditRecord record, CancellationToken cancellationToken = default)
    {
        await _persistentWorkflowAuditService.AppendAsync(record, cancellationToken);
    }

    public async Task<IReadOnlyList<WorkflowAuditRecord>> GetAllAsync(CancellationToken cancellationToken = default)
    {
        return await _persistentWorkflowAuditService.GetAllAsync(cancellationToken);
    }

    public async Task ClearAsync(CancellationToken cancellationToken = default)
    {
        await _persistentWorkflowAuditService.ClearAsync(cancellationToken);
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

# =========================
# BACKEND: orchestrator async audit
# =========================

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
        => await RunCaseActionAsync("SubmitCase", caseId, "InReview", role, _workflowTransitionService.SubmitCaseAsync, cancellationToken);

    public async Task<(Guid Id, string Status)> ApproveCaseAsync(Guid caseId, string? role, CancellationToken cancellationToken = default)
        => await RunCaseActionAsync("ApproveCase", caseId, "Approved", role, _workflowTransitionService.ApproveCaseAsync, cancellationToken);

    public async Task<(Guid Id, string Status)> RejectCaseAsync(Guid caseId, string? role, CancellationToken cancellationToken = default)
        => await RunCaseActionAsync("RejectCase", caseId, "Rejected", role, _workflowTransitionService.RejectCaseAsync, cancellationToken);

    public async Task<(Guid Id, string Status)> PreparePublicationAsync(Guid caseId, string? role, CancellationToken cancellationToken = default)
        => await RunCaseActionAsync("PreparePublication", caseId, "ReadyForPublication", role, _workflowTransitionService.PreparePublicationAsync, cancellationToken);

    public async Task<(Guid Id, string Status)> PublishCaseAsync(Guid caseId, string? role, CancellationToken cancellationToken = default)
        => await RunCaseActionAsync("PublishCase", caseId, "Published", role, _workflowTransitionService.PublishCaseAsync, cancellationToken);

    public async Task<(Guid Id, string Status)> HoldCaseAsync(Guid caseId, string? role, CancellationToken cancellationToken = default)
        => await RunCaseActionAsync("HoldCase", caseId, "OnHold", role, _workflowTransitionService.HoldCaseAsync, cancellationToken);

    public async Task<(Guid Id, string Status)> ResolveContradictionAsync(Guid contradictionId, string? role, CancellationToken cancellationToken = default)
        => await RunContradictionActionAsync("ResolveContradiction", contradictionId, "Resolved", role, _workflowTransitionService.ResolveContradictionAsync, cancellationToken);

    public async Task<(Guid Id, string Status)> EscalateContradictionAsync(Guid contradictionId, string? role, CancellationToken cancellationToken = default)
        => await RunContradictionActionAsync("EscalateContradiction", contradictionId, "UnderReview", role, _workflowTransitionService.EscalateContradictionAsync, cancellationToken);

    public async Task<(Guid Id, string Status)> SendClaimToReviewAsync(Guid claimId, string? role, CancellationToken cancellationToken = default)
        => await RunClaimActionAsync("SendClaimToReview", claimId, "InReview", role, _workflowTransitionService.SendClaimToReviewAsync, cancellationToken);

    public async Task<(Guid Id, string Status)> ReturnClaimForEditAsync(Guid claimId, string? role, CancellationToken cancellationToken = default)
        => await RunClaimActionAsync("ReturnClaimForEdit", claimId, "Draft", role, _workflowTransitionService.ReturnClaimForEditAsync, cancellationToken);

    public async Task<(Guid Id, string Status)> CompleteReviewAsync(Guid reviewId, string? role, CancellationToken cancellationToken = default)
        => await RunReviewActionAsync("CompleteReview", reviewId, "Completed", role, _workflowTransitionService.CompleteReviewAsync, cancellationToken);

    public async Task<(Guid Id, string Status)> ReopenReviewAsync(Guid reviewId, string? role, CancellationToken cancellationToken = default)
        => await RunReviewActionAsync("ReopenReview", reviewId, "Open", role, _workflowTransitionService.ReopenReviewAsync, cancellationToken);

    public async Task<(Guid CaseId, Guid ClaimAId, Guid ClaimBId, Guid ContradictionId, string CaseStatus, string ContradictionStatus)> SeedLifecycleAsync(string? role, CancellationToken cancellationToken = default)
    {
        _workflowIntegrityService.EnsureRoleAllowed("SeedLifecycle", role);

        var seeded = await _workflowTransitionService.SeedLifecycleAsync(cancellationToken);

        await _workflowAuditStore.AddAsync(new WorkflowAuditRecord(
            Guid.NewGuid(),
            "Seed",
            seeded.CaseId,
            "SeedLifecycle",
            null,
            seeded.CaseStatus,
            NormalizeRole(role),
            true,
            "Seeded lifecycle scenario.",
            DateTime.UtcNow), cancellationToken);

        return seeded;
    }

    private async Task<(Guid Id, string Status)> RunCaseActionAsync(string actionName, Guid caseId, string nextStatus, string? role, Func<Guid, CancellationToken, Task<(Guid Id, string Status)>> operation, CancellationToken cancellationToken)
    {
        var entity = await _dbContext.Cases.FirstOrDefaultAsync(x => x.Id == caseId, cancellationToken)
            ?? throw new InvalidOperationException($"Case '{caseId}' was not found.");

        var previous = entity.Status.ToString();
        _workflowIntegrityService.EnsureRoleAllowed(actionName, role);
        _workflowIntegrityService.EnsureTransitionAllowed("Case", previous, nextStatus);

        try
        {
            var result = await operation(caseId, cancellationToken);
            await _workflowAuditStore.AddAsync(new WorkflowAuditRecord(Guid.NewGuid(), "Case", caseId, actionName, previous, result.Status, NormalizeRole(role), true, "Transition succeeded.", DateTime.UtcNow), cancellationToken);
            return result;
        }
        catch (Exception ex)
        {
            await _workflowAuditStore.AddAsync(new WorkflowAuditRecord(Guid.NewGuid(), "Case", caseId, actionName, previous, nextStatus, NormalizeRole(role), false, ex.Message, DateTime.UtcNow), cancellationToken);
            throw;
        }
    }

    private async Task<(Guid Id, string Status)> RunClaimActionAsync(string actionName, Guid claimId, string nextStatus, string? role, Func<Guid, CancellationToken, Task<(Guid Id, string Status)>> operation, CancellationToken cancellationToken)
    {
        var entity = await _dbContext.Claims.FirstOrDefaultAsync(x => x.Id == claimId, cancellationToken)
            ?? throw new InvalidOperationException($"Claim '{claimId}' was not found.");

        var previous = entity.Status.ToString();
        _workflowIntegrityService.EnsureRoleAllowed(actionName, role);
        _workflowIntegrityService.EnsureTransitionAllowed("Claim", previous, nextStatus);

        try
        {
            var result = await operation(claimId, cancellationToken);
            await _workflowAuditStore.AddAsync(new WorkflowAuditRecord(Guid.NewGuid(), "Claim", claimId, actionName, previous, result.Status, NormalizeRole(role), true, "Transition succeeded.", DateTime.UtcNow), cancellationToken);
            return result;
        }
        catch (Exception ex)
        {
            await _workflowAuditStore.AddAsync(new WorkflowAuditRecord(Guid.NewGuid(), "Claim", claimId, actionName, previous, nextStatus, NormalizeRole(role), false, ex.Message, DateTime.UtcNow), cancellationToken);
            throw;
        }
    }

    private async Task<(Guid Id, string Status)> RunContradictionActionAsync(string actionName, Guid contradictionId, string nextStatus, string? role, Func<Guid, CancellationToken, Task<(Guid Id, string Status)>> operation, CancellationToken cancellationToken)
    {
        var entity = await _dbContext.Contradictions.FirstOrDefaultAsync(x => x.Id == contradictionId, cancellationToken)
            ?? throw new InvalidOperationException($"Contradiction '{contradictionId}' was not found.");

        var previous = entity.Status.ToString();
        _workflowIntegrityService.EnsureRoleAllowed(actionName, role);
        _workflowIntegrityService.EnsureTransitionAllowed("Contradiction", previous, nextStatus);

        try
        {
            var result = await operation(contradictionId, cancellationToken);
            await _workflowAuditStore.AddAsync(new WorkflowAuditRecord(Guid.NewGuid(), "Contradiction", contradictionId, actionName, previous, result.Status, NormalizeRole(role), true, "Transition succeeded.", DateTime.UtcNow), cancellationToken);
            return result;
        }
        catch (Exception ex)
        {
            await _workflowAuditStore.AddAsync(new WorkflowAuditRecord(Guid.NewGuid(), "Contradiction", contradictionId, actionName, previous, nextStatus, NormalizeRole(role), false, ex.Message, DateTime.UtcNow), cancellationToken);
            throw;
        }
    }

    private async Task<(Guid Id, string Status)> RunReviewActionAsync(string actionName, Guid reviewId, string nextStatus, string? role, Func<Guid, CancellationToken, Task<(Guid Id, string Status)>> operation, CancellationToken cancellationToken)
    {
        var entity = await _dbContext.Reviews.FirstOrDefaultAsync(x => x.Id == reviewId, cancellationToken)
            ?? throw new InvalidOperationException($"Review '{reviewId}' was not found.");

        var previous = entity.Status.ToString();
        _workflowIntegrityService.EnsureRoleAllowed(actionName, role);
        _workflowIntegrityService.EnsureTransitionAllowed("Review", previous, nextStatus);

        try
        {
            var result = await operation(reviewId, cancellationToken);
            await _workflowAuditStore.AddAsync(new WorkflowAuditRecord(Guid.NewGuid(), "Review", reviewId, actionName, previous, result.Status, NormalizeRole(role), true, "Transition succeeded.", DateTime.UtcNow), cancellationToken);
            return result;
        }
        catch (Exception ex)
        {
            await _workflowAuditStore.AddAsync(new WorkflowAuditRecord(Guid.NewGuid(), "Review", reviewId, actionName, previous, nextStatus, NormalizeRole(role), false, ex.Message, DateTime.UtcNow), cancellationToken);
            throw;
        }
    }

    private static string NormalizeRole(string? role)
    {
        return string.IsNullOrWhiteSpace(role) ? "anonymous" : role.Trim().ToLowerInvariant();
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
    public async Task<ActionResult<IReadOnlyList<WorkflowAuditEntryResponse>>> GetEntries(CancellationToken cancellationToken)
    {
        var entries = (await _workflowAuditStore.GetAllAsync(cancellationToken))
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
    public async Task<IActionResult> Clear(CancellationToken cancellationToken)
    {
        await _workflowAuditStore.ClearAsync(cancellationToken);
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

# auth DI
$programPath = Join-Path $apiProj "Program.cs"
if (Test-Path $programPath) {
    $program = Get-Content $programPath -Raw

    if ($program -notmatch 'WorkflowRequestContext') {
        $program = $program -replace '(builder\.Services\.[^\r\n;]+;)', '$1
builder.Services.AddSingleton<VeritasAtlas.Api.Infrastructure.WorkflowRequestContext>();
builder.Services.AddSingleton<VeritasAtlas.Api.Infrastructure.WorkflowAuthorizationService>();'
    }

    Write-File -Path $programPath -Content $program
}

if (Test-Path $diPath) {
    $diContent = Get-Content $diPath -Raw

    if ($diContent -notmatch 'PersistentWorkflowAuditService') {
        $diContent = $diContent -replace '(services\.AddSingleton<WorkflowAuditStore>\(\);)', 'services.AddSingleton<PersistentWorkflowAuditService>();
        $1'
    }

    Write-File -Path $diPath -Content $diContent
}

# tests
Write-File -Path (Join-Path $tools "tests\Run-Phase10-Verification.ps1") -Content @'
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
        $response = Invoke-WebRequest -Uri $Url -Method $Method -Headers $headers -UseBasicParsing -TimeoutSec 20
        $body = $response.Content
        $json = $null
        try { $json = $body | ConvertFrom-Json } catch {}
        return @{
            Success = $true
            StatusCode = [int]$response.StatusCode
            Data = $json
            Raw = $body
            Message = "OK"
        }
    }
    catch {
        $statusCode = -1
        $body = $_.Exception.Message

        if ($_.Exception.Response) {
            try { $statusCode = [int]$_.Exception.Response.StatusCode } catch {}
            try {
                $stream = $_.Exception.Response.GetResponseStream()
                if ($stream) {
                    $reader = New-Object System.IO.StreamReader($stream)
                    $body = $reader.ReadToEnd()
                }
            } catch {}
        }

        return @{
            Success = $false
            StatusCode = $statusCode
            Data = $null
            Raw = $body
            Message = $body
        }
    }
}

$timestamp = Get-Date -Format "yyyyMMdd-HHmmss"
$diag = Join-Path $RootDir "_diagnostics\phase10-tests-$timestamp"
Ensure-Dir $diag
$report = Join-Path $diag "phase10-verification-report.md"
$stdoutLog = Join-Path $diag "api-stdout.log"
$stderrLog = Join-Path $diag "api-stderr.log"

Set-Content -Path $report -Value "# Phase 10 Verification Report`r`n" -Encoding UTF8
Add-Content -Path $report -Value ("Generated: " + (Get-Date -Format "yyyy-MM-dd HH:mm:ss"))
Add-Content -Path $report -Value ""

$apiProcess = $null
$failed = $false

Push-Location (Join-Path $RootDir "apps\api\VeritasAtlas.Api")
try {
    $apiProcess = Start-Process "dotnet" -ArgumentList "run" -RedirectStandardOutput $stdoutLog -RedirectStandardError $stderrLog -PassThru
    Start-Sleep -Seconds 8

    $clear = Invoke-Api -Url "$BaseUrl/api/v1/workflow-audit/clear" -Method POST -Role "admin"
    Add-Result -ReportPath $report -Name "Clear audit" -Passed $clear.StatusCode -eq 200 -Detail $clear.Message

    $seedForbidden = Invoke-Api -Url "$BaseUrl/api/v1/actions/seed/lifecycle" -Method POST -Role "publisher"
    Add-Result -ReportPath $report -Name "Seed blocked for publisher" -Passed ($seedForbidden.StatusCode -eq 403) -Detail $seedForbidden.Message
    if ($seedForbidden.StatusCode -ne 403) { $failed = $true }

    $seed = Invoke-Api -Url "$BaseUrl/api/v1/actions/seed/lifecycle" -Method POST -Role "admin"
    Add-Result -ReportPath $report -Name "Seed lifecycle as admin" -Passed $seed.Success -Detail $seed.Message
    if (-not $seed.Success) { $failed = $true; throw "Seeding failed." }

    $caseId = $seed.Data.caseId
    $claimId = $seed.Data.primaryClaimId
    $contradictionId = $seed.Data.contradictionId

    $submit = Invoke-Api -Url "$BaseUrl/api/v1/actions/cases/$caseId/submit" -Method POST -Role "operator"
    Add-Result -ReportPath $report -Name "Submit case as operator" -Passed ($submit.StatusCode -eq 200) -Detail $submit.Raw
    if ($submit.StatusCode -ne 200) { $failed = $true }

    $approveForbidden = Invoke-Api -Url "$BaseUrl/api/v1/actions/cases/$caseId/approve" -Method POST -Role "operator"
    Add-Result -ReportPath $report -Name "Approve blocked for operator" -Passed ($approveForbidden.StatusCode -eq 403) -Detail $approveForbidden.Raw
    if ($approveForbidden.StatusCode -ne 403) { $failed = $true }

    $prepare = Invoke-Api -Url "$BaseUrl/api/v1/publication-workflow/cases/$caseId/prepare" -Method POST -Role "reviewer"
    Add-Result -ReportPath $report -Name "Prepare publication as reviewer" -Passed ($prepare.StatusCode -eq 200) -Detail $prepare.Raw
    if ($prepare.StatusCode -ne 200) { $failed = $true }

    $publishForbidden = Invoke-Api -Url "$BaseUrl/api/v1/publication-workflow/cases/$caseId/publish" -Method POST -Role "reviewer"
    Add-Result -ReportPath $report -Name "Publish blocked for reviewer" -Passed ($publishForbidden.StatusCode -eq 403) -Detail $publishForbidden.Raw
    if ($publishForbidden.StatusCode -ne 403) { $failed = $true }

    $publish = Invoke-Api -Url "$BaseUrl/api/v1/publication-workflow/cases/$caseId/publish" -Method POST -Role "publisher"
    Add-Result -ReportPath $report -Name "Publish as publisher" -Passed ($publish.StatusCode -eq 200) -Detail $publish.Raw
    if ($publish.StatusCode -ne 200) { $failed = $true }

    $publishRepeat = Invoke-Api -Url "$BaseUrl/api/v1/publication-workflow/cases/$caseId/publish" -Method POST -Role "publisher"
    Add-Result -ReportPath $report -Name "Repeat publish blocked" -Passed ($publishRepeat.StatusCode -eq 400) -Detail $publishRepeat.Raw
    if ($publishRepeat.StatusCode -ne 400) { $failed = $true }

    $claimReview = Invoke-Api -Url "$BaseUrl/api/v1/review-workflow/claims/$claimId/send-to-review" -Method POST -Role "reviewer"
    Add-Result -ReportPath $report -Name "Send claim to review" -Passed ($claimReview.StatusCode -eq 200) -Detail $claimReview.Raw
    if ($claimReview.StatusCode -ne 200) { $failed = $true }

    $claimReviewRepeat = Invoke-Api -Url "$BaseUrl/api/v1/review-workflow/claims/$claimId/send-to-review" -Method POST -Role "reviewer"
    Add-Result -ReportPath $report -Name "Repeat send-to-review blocked" -Passed ($claimReviewRepeat.StatusCode -eq 400) -Detail $claimReviewRepeat.Raw
    if ($claimReviewRepeat.StatusCode -ne 400) { $failed = $true }

    $claimReturn = Invoke-Api -Url "$BaseUrl/api/v1/review-workflow/claims/$claimId/return-for-edit" -Method POST -Role "reviewer"
    Add-Result -ReportPath $report -Name "Return claim for edit" -Passed ($claimReturn.StatusCode -eq 200) -Detail $claimReturn.Raw
    if ($claimReturn.StatusCode -ne 200) { $failed = $true }

    $escalate = Invoke-Api -Url "$BaseUrl/api/v1/review-workflow/contradictions/$contradictionId/escalate" -Method POST -Role "reviewer"
    Add-Result -ReportPath $report -Name "Escalate contradiction" -Passed ($escalate.StatusCode -eq 200) -Detail $escalate.Raw
    if ($escalate.StatusCode -ne 200) { $failed = $true }

    $resolve = Invoke-Api -Url "$BaseUrl/api/v1/actions/contradictions/$contradictionId/resolve" -Method POST -Role "reviewer"
    Add-Result -ReportPath $report -Name "Resolve contradiction" -Passed ($resolve.StatusCode -eq 200) -Detail $resolve.Raw
    if ($resolve.StatusCode -ne 200) { $failed = $true }

    $resolveRepeat = Invoke-Api -Url "$BaseUrl/api/v1/actions/contradictions/$contradictionId/resolve" -Method POST -Role "reviewer"
    Add-Result -ReportPath $report -Name "Repeat resolve blocked" -Passed ($resolveRepeat.StatusCode -eq 400) -Detail $resolveRepeat.Raw
    if ($resolveRepeat.StatusCode -ne 400) { $failed = $true }

    $rules = Invoke-Api -Url "$BaseUrl/api/v1/workflow-audit/rules" -Method GET -Role "admin"
    Add-Result -ReportPath $report -Name "Rules endpoint" -Passed ($rules.StatusCode -eq 200) -Detail $rules.Raw
    if ($rules.StatusCode -ne 200) { $failed = $true }

    $audit = Invoke-Api -Url "$BaseUrl/api/v1/workflow-audit/entries" -Method GET -Role "admin"
    $auditPass = $audit.StatusCode -eq 200 -and $audit.Data.Count -ge 6
    Add-Result -ReportPath $report -Name "Audit has persisted entries" -Passed $auditPass -Detail ($(if ($audit.StatusCode -eq 200) { "Entries: " + $audit.Data.Count } else { $audit.Raw }))
    if (-not $auditPass) { $failed = $true }
}
finally {
    Pop-Location
    if ($apiProcess -and -not $apiProcess.HasExited) {
        Stop-Process -Id $apiProcess.Id -Force
    }
}

if ($failed) {
    Write-Error "Phase 10 verification failed. See report: $report"
    exit 1
}
else {
    Write-Host "Phase 10 verification passed. Report: $report" -ForegroundColor Green
}
'@

# frontend pages
Write-File -Path (Join-Path $web "pages\RolePolicyPage.tsx") -Content @'
import { useWorkflowValidation } from "../hooks/useWorkflowValidation";

export function RolePolicyPage() {
  const query = useWorkflowValidation();

  if (query.isLoading) {
    return <div style={{ fontFamily: "Arial, sans-serif", padding: 24 }}>Loading role policies...</div>;
  }

  if (query.isError || !query.data) {
    return <div style={{ fontFamily: "Arial, sans-serif", padding: 24, color: "crimson" }}>Failed to load role policies.</div>;
  }

  return (
    <div style={{ fontFamily: "Arial, sans-serif", padding: 24 }}>
      <h1 style={{ marginTop: 0 }}>Role Policy</h1>
      <div style={panelStyle}>
        <ul style={{ marginBottom: 0 }}>
          {Object.entries(query.data.roles).map(([actionName, roles]) => (
            <li key={actionName}>
              <strong>{actionName}</strong>: {roles.join(", ")}
            </li>
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

Write-File -Path (Join-Path $web "pages\AuditPersistencePage.tsx") -Content @'
import { useWorkflowAuditEntries } from "../hooks/useWorkflowAudit";

export function AuditPersistencePage() {
  const query = useWorkflowAuditEntries();

  if (query.isLoading) {
    return <div style={{ fontFamily: "Arial, sans-serif", padding: 24 }}>Loading persisted audit...</div>;
  }

  if (query.isError) {
    return <div style={{ fontFamily: "Arial, sans-serif", padding: 24, color: "crimson" }}>Failed to load persisted audit.</div>;
  }

  return (
    <div style={{ fontFamily: "Arial, sans-serif", padding: 24 }}>
      <h1 style={{ marginTop: 0 }}>Audit Persistence</h1>
      <div style={panelStyle}>
        <p>Persisted entries: {query.data?.length ?? 0}</p>
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

Write-File -Path (Join-Path $web "pages\Phase10HardeningCenterPage.tsx") -Content @'
import { Link } from "react-router-dom";

export function Phase10HardeningCenterPage() {
  return (
    <div style={{ fontFamily: "Arial, sans-serif", padding: 24 }}>
      <h1 style={{ marginTop: 0 }}>Phase 10 Hardening Center</h1>
      <p style={{ color: "#555" }}>
        Production-readiness hub for authorization, persistent audit, workflow integrity, and verification.
      </p>

      <div style={gridStyle}>
        <Link to="/workflow-audit" style={cardStyle}>Workflow Audit</Link>
        <Link to="/audit-persistence" style={cardStyle}>Audit Persistence</Link>
        <Link to="/role-policy" style={cardStyle}>Role Policy</Link>
        <Link to="/workflow-validation" style={cardStyle}>Workflow Validation</Link>
        <Link to="/phase-9-integrity-center" style={cardStyle}>Phase 9 Integrity</Link>
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

$main = Join-Path $web "main.tsx"
$mainContent = Get-Content $main -Raw

$mainContent = Ensure-ImportLine -Content $mainContent -Anchor 'import { DashboardPage } from "./pages/DashboardPage";' -ImportLine 'import { RolePolicyPage } from "./pages/RolePolicyPage";'
$mainContent = Ensure-ImportLine -Content $mainContent -Anchor 'import { RolePolicyPage } from "./pages/RolePolicyPage";' -ImportLine 'import { AuditPersistencePage } from "./pages/AuditPersistencePage";'
$mainContent = Ensure-ImportLine -Content $mainContent -Anchor 'import { AuditPersistencePage } from "./pages/AuditPersistencePage";' -ImportLine 'import { Phase10HardeningCenterPage } from "./pages/Phase10HardeningCenterPage";'

$mainContent = Ensure-RouteBlock -Content $mainContent -AnchorRoute '{ path: "/dashboard", element: <DashboardPage /> },' -RouteBlock '{ path: "/role-policy", element: <RolePolicyPage /> },
  { path: "/audit-persistence", element: <AuditPersistencePage /> },
  { path: "/phase-10-hardening-center", element: <Phase10HardeningCenterPage /> },' -PresencePattern 'path: "/role-policy"'

$mainContent = Ensure-NavBlock -Content $mainContent -Anchor '<Link to="/workflow-audit">Workflow Audit</Link>' -NavBlock '<Link to="/role-policy">Role Policy</Link>
          <Link to="/audit-persistence">Audit Persistence</Link>
          <Link to="/phase-10-hardening-center">Phase 10 Hardening</Link>' -PresencePattern 'to="/role-policy"'

Write-File -Path $main -Content $mainContent

Write-File -Path (Join-Path $diag "phase-10-summary.md") -Content @'
# Phase 10 Summary

## Included
- workflow request role scaffolding
- workflow authorization helper
- persistent workflow audit service
- async audit store
- orchestrator hardening for persistent audit
- richer workflow error contract
- role policy and audit persistence UI pages
- phase 10 hardening center
- comprehensive automatic phase 10 verification runner

## Goal
Move the workflow layer closer to production readiness with persistent audit behavior, role-aware action flow, and stronger verification.
'@

Write-Host "Building..." -ForegroundColor Cyan
Build-All -RootDir $RootDir

Write-Host "Running comprehensive automatic Phase 10 tests..." -ForegroundColor Cyan
Invoke-Phase10Tests -RootDir $RootDir

Write-Host "Phase 10 bundle DONE" -ForegroundColor Green
