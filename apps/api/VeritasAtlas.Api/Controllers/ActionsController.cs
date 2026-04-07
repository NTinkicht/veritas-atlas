using Microsoft.AspNetCore.Mvc;

namespace VeritasAtlas.Api.Controllers;

[ApiController]
[Route("api/v1/actions")]
public class ActionsController : ControllerBase
{
    [HttpPost("cases/{caseId}/submit")]
    public IActionResult SubmitCase(Guid caseId)
    {
        return Ok(new { CaseId = caseId, Status = "Submitted", Timestamp = DateTime.UtcNow });
    }

    [HttpPost("cases/{caseId}/approve")]
    public IActionResult ApproveCase(Guid caseId)
    {
        return Ok(new { CaseId = caseId, Status = "Approved", Timestamp = DateTime.UtcNow });
    }

    [HttpPost("cases/{caseId}/reject")]
    public IActionResult RejectCase(Guid caseId)
    {
        return Ok(new { CaseId = caseId, Status = "Rejected", Timestamp = DateTime.UtcNow });
    }

    [HttpPost("contradictions/{id}/resolve")]
    public IActionResult ResolveContradiction(Guid id)
    {
        return Ok(new { ContradictionId = id, Status = "Resolved", Timestamp = DateTime.UtcNow });
    }

    [HttpPost("reviews/{id}/complete")]
    public IActionResult CompleteReview(Guid id)
    {
        return Ok(new { ReviewId = id, Status = "Completed", Timestamp = DateTime.UtcNow });
    }
}