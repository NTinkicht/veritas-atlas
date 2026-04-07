using Microsoft.AspNetCore.Mvc;

namespace VeritasAtlas.Api.Controllers;

[ApiController]
[Route("api/v1/review-workflow")]
public class ReviewWorkflowController : ControllerBase
{
    [HttpPost("claims/{claimId}/send-to-review")]
    public IActionResult SendClaimToReview(Guid claimId)
    {
        return Ok(new { ClaimId = claimId, Status = "InReview", Timestamp = DateTime.UtcNow });
    }

    [HttpPost("claims/{claimId}/return-for-edit")]
    public IActionResult ReturnClaimForEdit(Guid claimId)
    {
        return Ok(new { ClaimId = claimId, Status = "NeedsEdit", Timestamp = DateTime.UtcNow });
    }

    [HttpPost("contradictions/{id}/escalate")]
    public IActionResult EscalateContradiction(Guid id)
    {
        return Ok(new { ContradictionId = id, Status = "Escalated", Timestamp = DateTime.UtcNow });
    }

    [HttpPost("reviews/{id}/reopen")]
    public IActionResult ReopenReview(Guid id)
    {
        return Ok(new { ReviewId = id, Status = "Reopened", Timestamp = DateTime.UtcNow });
    }
}