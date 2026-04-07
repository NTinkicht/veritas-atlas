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