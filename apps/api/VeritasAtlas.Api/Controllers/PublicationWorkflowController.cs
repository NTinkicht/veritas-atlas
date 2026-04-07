using Microsoft.AspNetCore.Mvc;

namespace VeritasAtlas.Api.Controllers;

[ApiController]
[Route("api/v1/publication-workflow")]
public class PublicationWorkflowController : ControllerBase
{
    [HttpPost("cases/{caseId}/prepare")]
    public IActionResult PreparePublication(Guid caseId)
    {
        return Ok(new { CaseId = caseId, Status = "PreparedForPublication", Timestamp = DateTime.UtcNow });
    }

    [HttpPost("cases/{caseId}/publish")]
    public IActionResult PublishCase(Guid caseId)
    {
        return Ok(new { CaseId = caseId, Status = "Published", Timestamp = DateTime.UtcNow });
    }

    [HttpPost("cases/{caseId}/hold")]
    public IActionResult HoldCase(Guid caseId)
    {
        return Ok(new { CaseId = caseId, Status = "PublicationHold", Timestamp = DateTime.UtcNow });
    }
}