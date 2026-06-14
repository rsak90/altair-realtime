using Microsoft.AspNetCore.Mvc;
using SasJobRunner.Services;

namespace SasJobRunner.Controllers.Api;

/// <summary>
/// Server-Sent Events endpoint for log streaming (SSE fallback for SignalR).
/// With the poll-based architecture, this endpoint polls job status and streams
/// the complete log once the job reaches a terminal state.
/// </summary>
[ApiController]
public sealed class SseLogController(
    ISlcHubClient hubClient,
    ILogger<SseLogController> logger) : ControllerBase
{
    [HttpGet("api/session-jobs/{jobId}/log-stream")]
    public async Task StreamLog(string jobId, CancellationToken ct)
    {
        Response.ContentType = "text/event-stream";
        Response.Headers.CacheControl = "no-cache";

        try
        {
            // Poll job status until terminal state
            var maxWaitTime = TimeSpan.FromMinutes(10);
            var pollInterval = TimeSpan.FromSeconds(5);
            var startTime = DateTime.UtcNow;
            string status = "Running";

            while (status != "CompletedSuccess" && status != "CompletedError" && status != "Failed")
            {
                if (DateTime.UtcNow - startTime > maxWaitTime)
                {
                    logger.LogWarning("SSE stream for job {JobId} timed out after {MaxWait}", jobId, maxWaitTime);
                    await Response.WriteAsync($"data: {{\"error\": \"Job polling timeout\"}}\n\n", ct);
                    return;
                }
                await Task.Delay(pollInterval, ct);
                status = await hubClient.GetJobStatusAsync(jobId, ct);
                
                // Send status updates
                await Response.WriteAsync($"data: {{\"status\": \"{status}\"}}\n\n", ct);
                await Response.Body.FlushAsync(ct);
            }

            // Fetch and stream log content
            var logContent = await hubClient.GetJobLogAsync(jobId, ct);
            foreach (var line in logContent.Split('\n'))
            {
                await Response.WriteAsync($"data: {line}\n\n", ct);
                await Response.Body.FlushAsync(ct);
            }

            // Signal completion
            await Response.WriteAsync($"data: {{\"complete\": true}}\n\n", ct);
            await Response.Body.FlushAsync(ct);
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Error streaming log for job {JobId}", jobId);
            await Response.WriteAsync($"data: {{\"error\": \"{ex.Message}\"}}\n\n", ct);
        }
    }
}
