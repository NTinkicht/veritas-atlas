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
    private static readonly string[] AdminRoles = ["admin"];

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
        // Destructive: admin only, fail closed for every other or unknown role (#237).
        if (!TenantAnswerServeCheckController.HasExactlyOneRoleIn(User, AdminRoles))
        {
            return StatusCode(StatusCodes.Status403Forbidden);
        }

        await _scenarioPersistenceService.ClearSnapshotAsync();
        await _workflowAuditStore.ClearAsync(cancellationToken);

        return Ok(new ResetResponse(true, "Scenario snapshot and workflow audit have been cleared.", DateTime.UtcNow));
    }
}