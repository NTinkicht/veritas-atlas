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