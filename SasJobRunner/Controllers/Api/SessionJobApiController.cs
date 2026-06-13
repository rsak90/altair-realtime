using Microsoft.AspNetCore.Mvc;
using SasJobRunner.Models;
using SasJobRunner.Services;

namespace SasJobRunner.Controllers.Api;

/// <summary>
/// API controller for SAS job submission.
/// POST /api/session-jobs
/// </summary>
[ApiController]
[Route("api/session-jobs")]
public sealed class SessionJobApiController(
    ISessionJobOrchestrator orchestrator,
    ILogger<SessionJobApiController> logger) : ControllerBase
{
    /// <summary>
    /// Accepts a job submission request, delegates to <see cref="ISessionJobOrchestrator"/>,
    /// and returns the job identifier.
    /// </summary>
    [HttpPost]
    public async Task<IActionResult> SubmitJob(
        [FromBody] JobSubmitRequest request,
        CancellationToken ct)
    {
        var userId = HttpContext.Session.GetString("UserId");
        if (string.IsNullOrEmpty(userId))
            return BadRequest("UserId not in session.");

        try
        {
            var jobId = await orchestrator.SubmitAsync(userId, request.SessionId, request.SourceCode, ct);
            return Ok(new JobSubmitResponse(jobId));
        }
        catch (SlcHubException ex)
        {
            logger.LogWarning(ex, "SLC Hub returned non-success.");
            return StatusCode(502, ex.Message);
        }
    }
}
