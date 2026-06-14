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
            logger.LogError(ex, "SLC Hub returned non-success: Status={StatusCode}, Message={Message}", 
                ex.StatusCode, ex.Message);
            return StatusCode(502, new { 
                error = "SLC Hub Error", 
                statusCode = ex.StatusCode,
                detail = ex.Message 
            });
        }
        catch (InvalidOperationException ex) when (ex.Message.Contains("Bearer token"))
        {
            logger.LogError(ex, "Bearer token issue: {Message}", ex.Message);
            return StatusCode(401, new { 
                error = "Authentication Error", 
                detail = ex.Message 
            });
        }
        catch (HttpRequestException ex)
        {
            logger.LogError(ex, "Network error connecting to SLC Hub: {Message}", ex.Message);
            return StatusCode(503, new { 
                error = "Service Unavailable", 
                detail = "Cannot connect to SLC Hub. Please check the configuration and network connectivity." 
            });
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Unexpected error during job submission: {Message}", ex.Message);
            return StatusCode(500, new { 
                error = "Internal Server Error", 
                detail = ex.Message 
            });
        }
    }
}
