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
    IConfiguration configuration,
    ILogger<SessionJobOrchestrator> logger) : ISessionJobOrchestrator
{
    public async Task<string> SubmitAsync(
        string userId, string sessionId, string userSourceCode,
        CancellationToken ct = default)
    {
        // Ensure execution folder exists before submitting job
        var studyFolder = configuration["SessionStorage:StudyFolder"] 
            ?? throw new InvalidOperationException("SessionStorage:StudyFolder configuration is required.");
        var executionFolder = Path.Combine(studyFolder.TrimEnd('/'), "sessions", userId, sessionId);
        
        if (!Directory.Exists(executionFolder))
        {
            logger.LogInformation("Creating execution folder: {ExecutionFolder}", executionFolder);
            Directory.CreateDirectory(executionFolder);
        }

        // Register session with userId before GetAsync to enable immediate userId resolution
        // This avoids the need for filesystem scanning when MacroVarStore needs to construct file paths
        if (macroVarStore is MacroVarStore concreteStore)
        {
            concreteStore.RegisterSession(sessionId, userId);
        }

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

            while (status != "CompletedSuccess" && status != "CompletedError" && status != "Failed")
            {
                if (DateTime.UtcNow - startTime > maxWaitTime)
                {
                    logger.LogWarning("Job {JobId} timed out after {MaxWait}", jobId, maxWaitTime);
                    break;
                }
                await Task.Delay(pollInterval, ct);
                status = await hubClient.GetJobStatusAsync(jobId, ct);
            }

            logger.LogInformation("Job {JobId} reached terminal state: {Status}", jobId, status);

            // Fetch log content (main log)
            try
            {
                var logContent = await hubClient.GetJobLogAsync(jobId, ct);
                if (!string.IsNullOrEmpty(logContent))
                {
                    foreach (var line in logContent.Split('\n'))
                    {
                        logLines.Add(line);
                        await signalrContext.Clients.Group(jobId)
                            .SendAsync("ReceiveLog", line, ct);
                    }
                    logger.LogInformation("Job {JobId}: Streamed {Count} log lines from main log", jobId, logLines.Count);
                }
                else
                {
                    logger.LogWarning("Job {JobId}: Main log content is empty", jobId);
                }
            }
            catch (SlcHubException ex)
            {
                logger.LogError(ex, "Job {JobId}: Failed to fetch job log. Status code: {StatusCode}. Message: {Message}", 
                    jobId, ex.StatusCode, ex.Message);
                // Send error info to client but continue - maybe stderr has the log
                await signalrContext.Clients.Group(jobId)
                    .SendAsync("ReceiveLog", $"WARNING: Unable to fetch main job log (HTTP {ex.StatusCode})", ct);
            }
            catch (Exception ex)
            {
                logger.LogError(ex, "Job {JobId}: Unexpected error fetching job log", jobId);
                await signalrContext.Clients.Group(jobId)
                    .SendAsync("ReceiveLog", $"WARNING: Unexpected error fetching job log: {ex.Message}", ct);
            }

            // Fetch result files and process stderr (may contain the actual log for error cases)
            try
            {
                var results = await hubClient.GetJobResultsAsync(jobId, ct);
                logger.LogInformation("Job {JobId}: Found {Count} result files", jobId, results.Count);
                
                // For CompletedError, the log might be in stdout file
                var stdoutFile = results.FirstOrDefault(f => f.Name.Equals("stdout", StringComparison.OrdinalIgnoreCase));
                if (stdoutFile is not null)
                {
                    logger.LogInformation("Job {JobId}: Processing stdout file from {Url}", jobId, stdoutFile.Url);
                    var stdoutContent = await hubClient.GetResultFileContentAsync(stdoutFile.Url, ct);
                    if (!string.IsNullOrEmpty(stdoutContent))
                    {
                        var stdoutLines = stdoutContent.Split('\n');
                        foreach (var line in stdoutLines)
                        {
                            if (!logLines.Contains(line)) // Avoid duplicates
                            {
                                logLines.Add(line);
                                await signalrContext.Clients.Group(jobId)
                                    .SendAsync("ReceiveLog", line, ct);
                            }
                        }
                        logger.LogInformation("Job {JobId}: Processed {Count} lines from stdout", jobId, stdoutLines.Length);
                    }
                }
                
                var stderrFile = results.FirstOrDefault(f => f.Name.Equals("stderr", StringComparison.OrdinalIgnoreCase));
                if (stderrFile is not null)
                {
                    logger.LogInformation("Job {JobId}: Processing stderr file from {Url}", jobId, stderrFile.Url);
                    var stderrContent = await hubClient.GetResultFileContentAsync(stderrFile.Url, ct);
                    if (!string.IsNullOrEmpty(stderrContent))
                    {
                        var stderrLines = stderrContent.Split('\n');
                        foreach (var line in stderrLines)
                        {
                            var parsed = LogLine.Parse(line);
                            if (parsed.Severity != LogSeverity.Plain)
                            {
                                if (!logLines.Contains(line)) // Avoid duplicates
                                {
                                    logLines.Add(line);
                                    await signalrContext.Clients.Group(jobId)
                                        .SendAsync("ReceiveLog", line, ct);
                                }
                            }
                        }
                        logger.LogInformation("Job {JobId}: Processed stderr content, {Count} lines total", jobId, stderrLines.Length);
                    }
                }
                
                if (stdoutFile is null && stderrFile is null && logLines.Count == 0)
                {
                    logger.LogWarning("Job {JobId}: No log content found in main log, stdout, or stderr", jobId);
                    await signalrContext.Clients.Group(jobId)
                        .SendAsync("ReceiveLog", "WARNING: No log content available for this job", ct);
                }
            }
            catch (Exception ex)
            {
                logger.LogWarning(ex, "Job {JobId}: Failed to fetch or process result files", jobId);
                // Continue execution - result files are optional
            }

            await signalrContext.Clients.Group(jobId)
                .SendAsync("JobComplete", ct);

            // Parse and persist macro variables
            var newVars = logParser.ParseUserMacroVars(logLines);
            logger.LogInformation("Job {JobId}: Parsed {Count} macro variables from logs", jobId, newVars.Count);
            
            if (newVars.Count > 0)
            {
                logger.LogDebug("Job {JobId}: Calling SetAsync to persist {Count} variables for session {SessionId}", 
                    jobId, newVars.Count, sessionId);
                await macroVarStore.SetAsync(sessionId, newVars);
            }
            else
            {
                logger.LogDebug("Job {JobId}: No macro variables to persist for session {SessionId}", jobId, sessionId);
            }

            // Persist program history
            var summary  = LogParserService.Summarize(logLines);
            var datasets = ExtractDatasets(logLines);
            await historyStore.AddAsync(new ProgramHistoryRecord(
                Guid.NewGuid().ToString(), userId, sessionId,
                DateTime.UtcNow, userSourceCode, summary, datasets));

            // Notify clients that files may have changed
            if (datasets.Count > 0)
            {
                await signalrContext.Clients.Group($"session_{sessionId}")
                    .SendAsync("FilesChanged", ct);
            }
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
