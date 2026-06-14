using Microsoft.AspNetCore.SignalR;
using SasJobRunner.Hubs;
using SasJobRunner.Models;

namespace SasJobRunner.Services;

public sealed class SessionJobOrchestrator(
    ISlcHubClient hubClient,
    PreambleBuilder preambleBuilder,
    IMacroVarStore macroVarStore,
    IProgramHistoryStore historyStore,
    LogParserService logParser,
    IHubContext<LogStreamingHub> signalrContext,
    IHttpContextAccessor contextAccessor,
    ILogger<SessionJobOrchestrator> logger) : ISessionJobOrchestrator
{
    public async Task<string> SubmitAsync(
        string userId, string sessionId, string userSourceCode,
        CancellationToken ct = default)
    {
        var macroVars = await macroVarStore.GetAsync(sessionId);
        var preamble  = preambleBuilder.Build(userId, sessionId, macroVars);
        var full = preamble + Environment.NewLine
                 + userSourceCode + Environment.NewLine
                 + "%put _user_;";

        // Create job draft
        var jobId = await hubClient.CreateJobAsync(full, ct);
        // Commit job to start execution
        await hubClient.CommitJobAsync(jobId, ct);

        // Capture bearer token from current HTTP context before background task
        // This is necessary because StreamAndFinalizeAsync runs outside the HTTP request scope
        var bearerToken = contextAccessor.HttpContext?.Session.GetString("BearerToken");

        // Fire-and-forget: poll status, fetch logs, parse vars, persist history
        _ = StreamAndFinalizeAsync(userId, sessionId, jobId, userSourceCode, bearerToken, CancellationToken.None);

        return jobId;
    }

    private async Task StreamAndFinalizeAsync(
        string userId, string sessionId, string jobId,
        string userSourceCode, string? bearerToken, CancellationToken ct)
    {
        // Set bearer token for background task operations
        if (!string.IsNullOrEmpty(bearerToken))
        {
            hubClient.SetBearerToken(bearerToken);
        }

        var logLines = new List<string>();
        try
        {
            // Poll job status every 5 seconds until terminal state
            var maxWaitTime = TimeSpan.FromMinutes(10);
            var pollInterval = TimeSpan.FromSeconds(5);
            var startTime = DateTime.UtcNow;
            string status = "Running";

            while (status != "CompletedSuccess" && status != "Failed")
            {
                if (DateTime.UtcNow - startTime > maxWaitTime)
                {
                    logger.LogWarning("Job {JobId} timed out after {MaxWait}", jobId, maxWaitTime);
                    break;
                }
                await Task.Delay(pollInterval, ct);
                status = await hubClient.GetJobStatusAsync(jobId, ct);
            }

            // Fetch log content
            var logContent = await hubClient.GetJobLogAsync(jobId, ct);
            foreach (var line in logContent.Split('\n'))
            {
                logLines.Add(line);
                await signalrContext.Clients.Group(jobId)
                    .SendAsync("ReceiveLog", line, ct);
            }

            // Fetch result files and process stderr
            var results = await hubClient.GetJobResultsAsync(jobId, ct);
            var stderrFile = results.FirstOrDefault(f => f.Name.Equals("stderr", StringComparison.OrdinalIgnoreCase));
            if (stderrFile is not null)
            {
                var stderrContent = await hubClient.GetResultFileContentAsync(stderrFile.Url, ct);
                foreach (var line in stderrContent.Split('\n'))
                {
                    var parsed = LogLine.Parse(line);
                    if (parsed.Severity != LogSeverity.Plain)
                    {
                        logLines.Add(line);
                        await signalrContext.Clients.Group(jobId)
                            .SendAsync("ReceiveLog", line, ct);
                    }
                }
            }

            await signalrContext.Clients.Group(jobId)
                .SendAsync("JobComplete", ct);

            // Parse and persist macro variables
            var newVars = logParser.ParseUserMacroVars(logLines);
            if (newVars.Count > 0)
                await macroVarStore.SetAsync(sessionId, newVars);

            // Persist program history
            var summary  = LogParserService.Summarize(logLines);
            var datasets = ExtractDatasets(logLines);
            await historyStore.AddAsync(new ProgramHistoryRecord(
                Guid.NewGuid().ToString(), userId, sessionId,
                DateTime.UtcNow, userSourceCode, summary, datasets));
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Error streaming job {JobId}", jobId);
            await signalrContext.Clients.Group(jobId)
                .SendAsync("JobError", ex.Message, ct);
        }
    }

    private static IReadOnlyList<string> ExtractDatasets(List<string> logLines)
    {
        return logLines
            .Where(l => l.Contains("NOTE: The data set", StringComparison.OrdinalIgnoreCase))
            .Select(l =>
            {
                var start = l.IndexOf("SESSLIB.", StringComparison.OrdinalIgnoreCase);
                if (start < 0) return null;
                var rest = l[(start + 8)..];
                var end  = rest.IndexOf(' ');
                return end >= 0 ? rest[..end] : rest;
            })
            .OfType<string>()
            .Distinct()
            .ToList();
    }
}
