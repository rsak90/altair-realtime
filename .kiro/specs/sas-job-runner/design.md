# Design Document

## Feature: sas-job-runner

---

## Overview

SAS_Job_Runner is an ASP.NET Core MVC application (.NET 10) that wraps the Altair SLC Hub REST API. It provides a multi-page browser UI for authoring SAS programs in a Monaco editor, submitting jobs, streaming real-time logs via SignalR (with an SSE fallback), exploring output datasets, managing macro variables, and reviewing program history.

Authentication is handled via a service account + impersonation flow managed by the `TokenManager` singleton. On each request, `TokenManager.EnsureValidTokenAsync` checks the server-side ASP.NET Session for a valid Bearer token; if absent or expired it logs in with service account credentials and then impersonates the configured user to obtain a scoped token. Every outbound request to the Altair SLC Hub is made server-side with the `Authorization: Bearer {token}` header attached by `SlcHubClient`. Persistence (Redis for macro variables, PostgreSQL for program history) is replaced by in-memory singleton mocks for this phase.

### Key Design Decisions

- **No external infrastructure** — `MacroVarStore` and `ProgramHistoryStore` are interface-backed singleton in-memory stores, swappable for Redis/PostgreSQL without touching the service layer.
- **Bearer token lives only in server-side Session** — the token never travels to the browser; all Hub calls are made server-side.
- **SignalR + SSE dual streaming path** — SignalR is the primary channel; the JavaScript client falls back to the SSE endpoint automatically when SignalR is unavailable.
- **Plain CSS, no framework** — all styling uses a single `site.css`; Razor shared layout handles navigation and session selector.
- **Monaco via CDN** — loaded from `cdn.jsdelivr.net/npm/monaco-editor` to avoid a build pipeline dependency.

---

## Architecture

### High-Level System Diagram

```
┌─────────────────────────────────────────────────────────────────────────┐
│  Browser                                                                │
│  ┌──────────────┐  ┌──────────────┐  ┌─────────────┐  ┌─────────────┐ │
│  │ Monaco Editor│  │  Log Viewer  │  │  Dataset    │  │  Macro Var  │ │
│  │  (SAS code)  │  │ (SSE/SignalR)│  │  Explorer   │  │   Panel     │ │
│  └──────┬───────┘  └──────┬───────┘  └──────┬──────┘  └──────┬──────┘ │
│         │  POST /api/     │  SignalR /hubs/  │  Ajax /api/    │  Ajax  │
│         │  session-jobs   │  log  or SSE     │  session-jobs  │        │
└─────────┼─────────────────┼─────────────────┼────────────────┼────────┘
          │                 │                 │                │
┌─────────▼─────────────────▼─────────────────▼────────────────▼────────┐
│  ASP.NET Core MVC Application (.NET 10)                                │
│                                                                        │
│  ┌──────────────────────┐   ┌──────────────────────────────────────┐  │
│  │  MVC Controllers     │   │  API Controllers                     │  │
│  │  EditorController    │   │  SessionJobApiController             │  │
│  │  SessionController   │   │  SseLogController                    │  │
│  │  DatasetController   │   └──────────────────────────────────────┘  │
│  │  HistoryController   │                                              │
│  └──────────────────────┘   ┌──────────────────────────────────────┐  │
│                             │  SignalR Hub                          │  │
│                             │  LogStreamingHub (/hubs/log)         │  │
│                             └──────────────────────────────────────┘  │
│                                                                        │
│  ┌──────────────────────────────────────────────────────────────────┐  │
│  │  Service Layer                                                   │  │
│  │  SessionJobOrchestrator  │  PreambleBuilder                     │  │
│  │  LogParserService        │  LogStreamer                         │  │
│  │  TokenManager            │                                      │  │
│  └──────────────────────────────────────────────────────────────────┘  │
│                                                                        │
│  ┌──────────────────┐  ┌──────────────────┐  ┌─────────────────────┐  │
│  │ SlcHubClient     │  │ MacroVarStore    │  │ ProgramHistoryStore │  │
│  │ (typed HttpClient│  │ (in-memory mock) │  │ (in-memory mock)    │  │
│  │  + Bearer token) │  │                  │  │                     │  │
│  └────────┬─────────┘  └──────────────────┘  └─────────────────────┘  │
│           │                                                            │
│  ASP.NET Session (Bearer token per session — acquired via service account impersonation) │
└───────────┼────────────────────────────────────────────────────────────┘
            │  HTTPS + Authorization: Bearer {token}
┌───────────▼───────────────────┐
│  Altair SLC Hub REST API      │
│  POST /api/v2/auth/login      │
│  POST /api/v2/auth/impersonate│
│  POST /api/v2/namespaces/{ns}/jobs             │
│  POST /api/v2/namespaces/{ns}/jobs/{id}/commit │
│  GET  /api/v2/namespaces/{ns}/jobs/{id}        │
│  GET  /api/v2/namespaces/{ns}/jobs/{id}/log    │
│  GET  /api/v2/namespaces/{ns}/jobs/{id}/results│
└───────────────────────────────┘
```

### Project Structure

```
SasJobRunner/
├── Program.cs
├── appsettings.json
├── appsettings.Development.json
│
├── Controllers/
│   ├── HomeController.cs              # Redirects to /Editor
│   ├── EditorController.cs            # GET /Editor
│   ├── SessionController.cs           # GET/POST /Session (new + resume)
│   ├── DatasetController.cs           # GET /Dataset
│   ├── HistoryController.cs           # GET /History
│   ├── Api/
│   │   ├── SessionJobApiController.cs # POST /api/session-jobs
│   │   ├── FilesApiController.cs      # GET /api/files, POST /api/files/{fileName}/view
│   │   └── SseLogController.cs        # GET  /api/session-jobs/{jobId}/log-stream
│
├── Hubs/
│   └── LogStreamingHub.cs             # SignalR hub at /hubs/log
│
├── Services/
│   ├── ISlcHubClient.cs
│   ├── SlcHubClient.cs
│   ├── ISessionJobOrchestrator.cs
│   ├── SessionJobOrchestrator.cs
│   ├── ITokenManager.cs
│   ├── TokenManager.cs
│   ├── PreambleBuilder.cs
│   ├── LogParserService.cs
│   ├── IMacroVarStore.cs
│   ├── MacroVarStore.cs
│   ├── IProgramHistoryStore.cs
│   └── ProgramHistoryStore.cs
│
├── Models/
│   ├── SessionInfo.cs
│   ├── JobSubmitRequest.cs
│   ├── JobSubmitResponse.cs
│   ├── JobResultFile.cs
│   ├── DatasetFileInfo.cs
│   ├── LogLine.cs
│   ├── MacroVar.cs
│   ├── ProgramHistoryRecord.cs
│   ├── DatasetRow.cs
│   └── PagedResult.cs
│
├── ViewModels/
│   ├── EditorViewModel.cs
│   ├── SessionViewModel.cs
│   ├── DatasetViewModel.cs
│   └── HistoryViewModel.cs
│
├── Views/
│   ├── Shared/
│   │   └── _Layout.cshtml
│   ├── Editor/
│   │   └── Index.cshtml
│   ├── Session/
│   │   └── Index.cshtml
│   ├── Dataset/
│   │   └── Index.cshtml
│   └── History/
│       └── Index.cshtml
│
└── wwwroot/
    ├── css/
    │   └── site.css
    └── js/
        ├── editor.js        # Monaco init + submit wiring
        ├── log-viewer.js    # SignalR/SSE connection + color coding
        ├── dataset.js       # Sort + pagination
        ├── macro-panel.js   # Inline editing + %let submission
        └── file-browser.js  # Files tab + dataset viewer modal
```

### Data Flow: Job Submission

```
Browser                 SessionJobApiController   SessionJobOrchestrator   SlcHubClient   SLC Hub
  │  POST /api/session-jobs {sessionId, source}  │                     │               │
  ├─────────────────────►│  SubmitAsync(...)      │                     │               │
  │                      ├───────────────────────►│  GetAsync(sessionId)│               │
  │                      │                        ├────────────────────►│               │
  │                      │                        │ PreambleBuilder.Build(...)           │
  │                      │                        │ assemble full code  │               │
  │                      │                        │ CreateJobAsync(full)│  POST .../jobs│
  │                      │                        ├────────────────────►├──────────────►│
  │                      │                        │ jobId (draft)       │◄──────────────┤
  │                      │                        │ CommitJobAsync(jobId)│ POST .../commit│
  │                      │                        ├────────────────────►├──────────────►│
  │                      │                        │◄────────────────────┤◄──────────────┤
  │                      │◄───────────────────────┤ (bg: poll & stream) │               │
  │  200 {jobId}         │◄───────────────────────┤                     │               │
  │◄─────────────────────┤                        │                     │               │
```

### Data Flow: Job Status Polling and Log Retrieval

```
SessionJobOrchestrator (bg)    SlcHubClient        SLC Hub      LogStreamingHub     Browser SignalR
      │  Poll loop (every 5s)          │                  │                   │                   │
      ├─────────────►│  GetJobStatusAsync(jobId)          │                   │                   │
      │              ├────────────────►│  GET .../jobs/{id}                   │                   │
      │   "Running"  │◄────────────────┤                   │                   │                   │
      │◄─────────────┤                 │                   │                   │                   │
      │  (wait 5s)   │                 │                   │                   │                   │
      ├─────────────►│  GetJobStatusAsync(jobId)          │                   │                   │
      │              ├────────────────►│  GET .../jobs/{id}                   │                   │
      │  "Completed" │◄────────────────┤                   │                   │                   │
      │◄─────────────┤                 │                   │                   │                   │
      │ GetJobLogAsync(jobId)          │                   │                   │                   │
      ├─────────────►│ GET .../jobs/{id}/log               │                   │                   │
      │              ├────────────────►│                   │                   │                   │
      │  log content │◄────────────────┤                   │                   │                   │
      │◄─────────────┤                 │                   │                   │                   │
      │  split lines + forward each    │                   │                   │                   │
      ├──────────────────────────────────────────────────►│  SendAsync("ReceiveLog", line)        │
      │              │                 │                   ├──────────────────────────────────────►│
      │ GetJobResultsAsync(jobId)      │                   │                   │                   │
      ├─────────────►│ GET .../jobs/{id}/results           │                   │                   │
      │              ├────────────────►│                   │                   │                   │
      │  [{Name:"stderr",Url:"..."}]   │◄────────────────┤                   │                   │
      │◄─────────────┤                 │                   │                   │                   │
      │ GetResultFileContentAsync(url) │                   │                   │                   │
      ├─────────────►│ GET file URL    │                   │                   │                   │
      │              ├────────────────►│                   │                   │                   │
      │  stderr content│◄───────────────┤                   │                   │                   │
      │◄─────────────┤                 │                   │                   │                   │
      │  parse ERROR/WARNING/NOTE lines│                   │                   │                   │
      ├──────────────────────────────────────────────────►│  SendAsync("ReceiveLog", diagnostic)  │
      │              │                 │                   ├──────────────────────────────────────►│
      ├──────────────────────────────────────────────────►│  SendAsync("JobComplete")             │
      │              │                 │                   ├──────────────────────────────────────►│
```

### MVC Routes

| Method | Route | Controller Action | Response |
|--------|-------|------------------|----------|
| GET | `/` | `HomeController.Index` | Redirect → `/Editor` |
| GET | `/Editor` | `EditorController.Index` | View |
| GET | `/Session` | `SessionController.Index` | View |
| POST | `/Session/New` | `SessionController.New` | Redirect → `/Editor` |
| POST | `/Session/Resume` | `SessionController.Resume` | Redirect → `/Editor` |
| GET | `/Dataset` | `DatasetController.Index` | View |
| GET | `/History` | `HistoryController.Index` | View |
| POST | `/api/session-jobs` | `SessionJobApiController.SubmitJob` | JSON |
| GET | `/api/session-jobs/{jobId}/log-stream` | `SseLogController.StreamLog` | SSE |
| GET | `/api/files` | `FilesApiController.ListFiles` | JSON |
| POST | `/api/files/{fileName}/view` | `FilesApiController.ViewDataset` | JSON |
| WS | `/hubs/log` | `LogStreamingHub` | SignalR |

---

## Components and Interfaces

### TokenManager

A singleton service responsible for obtaining and refreshing Bearer tokens via the service-account-based authentication flow. Stores the impersonate token, user token, and expiry metadata in ASP.NET Session.

```csharp
public interface ITokenManager
{
    /// <summary>
    /// Ensures a valid user token is present in session.
    /// If absent or expired, acquires a new token via login + impersonate flow.
    /// Returns the valid user token.
    /// </summary>
    Task<string> EnsureValidTokenAsync(CancellationToken ct = default);
}

public sealed class TokenManager(
    HttpClient httpClient,
    IHttpContextAccessor contextAccessor,
    IConfiguration configuration,
    ILogger<TokenManager> logger) : ITokenManager
{
    public async Task<string> EnsureValidTokenAsync(CancellationToken ct = default)
    {
        var session = contextAccessor.HttpContext!.Session;
        var userToken = session.GetString("BearerToken");
        var expiresIn = session.GetInt32("BearerTokenExpiresIn");
        var acquiredAt = session.GetString("BearerTokenAcquiredAt");

        if (userToken is not null && expiresIn is not null && acquiredAt is not null)
        {
            var acquired = DateTime.Parse(acquiredAt);
            var elapsed  = (DateTime.UtcNow - acquired).TotalSeconds;
            if (elapsed < expiresIn.Value)
                return userToken;
        }

        // Token expired or absent — acquire new token
        var impersonateToken = await LoginAsync(ct);
        var userId = configuration["SlcHub:UserId"]!;
        userToken = await ImpersonateAsync(impersonateToken, userId, ct);

        session.SetString("BearerToken", userToken);
        session.SetInt32("BearerTokenExpiresIn", 3600); // assume 1 hour
        session.SetString("BearerTokenAcquiredAt", DateTime.UtcNow.ToString("O"));

        return userToken;
    }

    private async Task<string> LoginAsync(CancellationToken ct)
    {
        var username = configuration["SlcHub:ServiceAccount:Username"]!;
        var password = configuration["SlcHub:ServiceAccount:Password"]!;
        var response = await httpClient.PostAsJsonAsync(
            "/api/v2/auth/login",
            new { username, password },
            ct);
        if (!response.IsSuccessStatusCode)
        {
            var body = await response.Content.ReadAsStringAsync(ct);
            logger.LogError("Login failed: {Body}", body);
            throw new InvalidOperationException($"Login failed: {body}");
        }
        var result = await response.Content.ReadFromJsonAsync<TokenResponse>(ct);
        return result!.Token;
    }

    private async Task<string> ImpersonateAsync(string impersonateToken, string userId, CancellationToken ct)
    {
        httpClient.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", impersonateToken);
        var response = await httpClient.PostAsJsonAsync(
            "/api/v2/auth/impersonate",
            new { userId },
            ct);
        if (!response.IsSuccessStatusCode)
        {
            var body = await response.Content.ReadAsStringAsync(ct);
            logger.LogError("Impersonate failed: {Body}", body);
            throw new InvalidOperationException($"Impersonate failed: {body}");
        }
        var result = await response.Content.ReadFromJsonAsync<TokenResponse>(ct);
        return result!.Token;
    }

    private record TokenResponse(string Token, int ExpiresIn);
}
```

### SlcHubClient

A typed `HttpClient` wrapper registered via `AddHttpClient<ISlcHubClient, SlcHubClient>`. On every outbound request it retrieves the Bearer token from `IHttpContextAccessor → HttpContext.Session["BearerToken"]` and injects `Authorization: Bearer {token}`.

```csharp
public interface ISlcHubClient
{
    /// <summary>
    /// Creates a job draft and commits it, returning the jobId.
    /// Throws SlcHubException on non-2xx response.
    /// </summary>
    Task<string> CreateJobAsync(string assembledCode, CancellationToken ct = default);

    /// <summary>
    /// Commits a job draft, starting execution.
    /// Throws SlcHubException on non-2xx response.
    /// </summary>
    Task CommitJobAsync(string jobId, CancellationToken ct = default);

    /// <summary>
    /// Polls job status endpoint and returns the JobStatus string.
    /// Throws SlcHubException on non-2xx response.
    /// </summary>
    Task<string> GetJobStatusAsync(string jobId, CancellationToken ct = default);

    /// <summary>
    /// Retrieves the full log content for a completed job.
    /// Throws SlcHubException on non-2xx response.
    /// </summary>
    Task<string> GetJobLogAsync(string jobId, CancellationToken ct = default);

    /// <summary>
    /// Retrieves the result file listing for a completed job.
    /// Returns empty list if no results are available.
    /// </summary>
    Task<IReadOnlyList<JobResultFile>> GetJobResultsAsync(string jobId, CancellationToken ct = default);

    /// <summary>
    /// Fetches the content of a result file by URL.
    /// Throws SlcHubException on non-2xx response.
    /// </summary>
    Task<string> GetResultFileContentAsync(string fileUrl, CancellationToken ct = default);
}

public sealed class SlcHubException(string message, int statusCode) : Exception(message)
{
    public int StatusCode { get; } = statusCode;
}

public sealed class SlcHubClient(
    HttpClient httpClient,
    IHttpContextAccessor contextAccessor,
    IConfiguration configuration,
    ILogger<SlcHubClient> logger) : ISlcHubClient
{
    private void ApplyBearerToken()
    {
        var token = contextAccessor.HttpContext!.Session.GetString("BearerToken")
            ?? throw new InvalidOperationException("Bearer token not found in session.");
        httpClient.DefaultRequestHeaders.Authorization =
            new AuthenticationHeaderValue("Bearer", token);
    }

    public async Task<string> CreateJobAsync(string assembledCode, CancellationToken ct = default)
    {
        ApplyBearerToken();
        var ns = configuration["SlcHub:Namespace"]!;
        var profile = configuration["SlcHub:ExecutionProfile"]!;
        var response = await httpClient.PostAsJsonAsync(
            $"/api/v2/namespaces/{ns}/jobs",
            new { code = assembledCode, executionProfile = profile },
            ct);
        if (!response.IsSuccessStatusCode)
        {
            var body = await response.Content.ReadAsStringAsync(ct);
            throw new SlcHubException(body, (int)response.StatusCode);
        }
        var result = await response.Content.ReadFromJsonAsync<JobIdResult>(ct);
        return result!.JobId;
    }

    public async Task CommitJobAsync(string jobId, CancellationToken ct = default)
    {
        ApplyBearerToken();
        var ns = configuration["SlcHub:Namespace"]!;
        var response = await httpClient.PostAsync(
            $"/api/v2/namespaces/{ns}/jobs/{jobId}/commit",
            null,
            ct);
        if (!response.IsSuccessStatusCode)
        {
            var body = await response.Content.ReadAsStringAsync(ct);
            throw new SlcHubException(body, (int)response.StatusCode);
        }
    }

    public async Task<string> GetJobStatusAsync(string jobId, CancellationToken ct = default)
    {
        ApplyBearerToken();
        var ns = configuration["SlcHub:Namespace"]!;
        var response = await httpClient.GetAsync(
            $"/api/v2/namespaces/{ns}/jobs/{jobId}",
            ct);
        if (!response.IsSuccessStatusCode)
        {
            var body = await response.Content.ReadAsStringAsync(ct);
            throw new SlcHubException(body, (int)response.StatusCode);
        }
        var result = await response.Content.ReadFromJsonAsync<JobStatusResult>(ct);
        return result!.Status;
    }

    public async Task<string> GetJobLogAsync(string jobId, CancellationToken ct = default)
    {
        ApplyBearerToken();
        var ns = configuration["SlcHub:Namespace"]!;
        var response = await httpClient.GetAsync(
            $"/api/v2/namespaces/{ns}/jobs/{jobId}/log",
            ct);
        if (!response.IsSuccessStatusCode)
        {
            var body = await response.Content.ReadAsStringAsync(ct);
            throw new SlcHubException(body, (int)response.StatusCode);
        }
        return await response.Content.ReadAsStringAsync(ct);
    }

    public async Task<IReadOnlyList<JobResultFile>> GetJobResultsAsync(string jobId, CancellationToken ct = default)
    {
        ApplyBearerToken();
        var ns = configuration["SlcHub:Namespace"]!;
        var response = await httpClient.GetAsync(
            $"/api/v2/namespaces/{ns}/jobs/{jobId}/results",
            ct);
        if (!response.IsSuccessStatusCode)
            return Array.Empty<JobResultFile>();
        var result = await response.Content.ReadFromJsonAsync<ResultsResponse>(ct);
        return result?.Files ?? Array.Empty<JobResultFile>();
    }

    public async Task<string> GetResultFileContentAsync(string fileUrl, CancellationToken ct = default)
    {
        ApplyBearerToken();
        var response = await httpClient.GetAsync(fileUrl, ct);
        if (!response.IsSuccessStatusCode)
        {
            var body = await response.Content.ReadAsStringAsync(ct);
            throw new SlcHubException(body, (int)response.StatusCode);
        }
        return await response.Content.ReadAsStringAsync(ct);
    }

    private record JobIdResult(string JobId);
    private record JobStatusResult(string Status);
    private record ResultsResponse(IReadOnlyList<JobResultFile> Files);
}
```

### SessionJobOrchestrator

The central coordination service. Assembles the full SAS program, submits it, starts background log streaming, and returns the `jobId`.

```csharp
public interface ISessionJobOrchestrator
{
    /// <summary>
    /// Assembles preamble + user code + trailer, submits to Hub,
    /// starts background log streaming, and returns the jobId.
    /// </summary>
    Task<string> SubmitAsync(
        string userId,
        string sessionId,
        string userSourceCode,
        CancellationToken ct = default);
}

public sealed class SessionJobOrchestrator(
    ISlcHubClient hubClient,
    PreambleBuilder preambleBuilder,
    IMacroVarStore macroVarStore,
    IProgramHistoryStore historyStore,
    LogParserService logParser,
    IHubContext<LogStreamingHub> signalrContext,
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

        // Fire-and-forget: poll status, fetch logs, parse vars, persist history
        _ = StreamAndFinalizeAsync(userId, sessionId, jobId, userSourceCode, CancellationToken.None);

        return jobId;
    }

    private async Task StreamAndFinalizeAsync(
        string userId, string sessionId, string jobId,
        string userSourceCode, CancellationToken ct)
    {
        var logLines = new List<string>();
        try
        {
            // Poll job status every 5 seconds until terminal state
            var maxWaitTime = TimeSpan.FromMinutes(10);
            var pollInterval = TimeSpan.FromSeconds(5);
            var startTime = DateTime.UtcNow;
            string status = "Running";

            while (status != "Completed" && status != "Failed")
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
        // Parse NOTE: The data set SESSLIB.name has N observations lines
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
```

### PreambleBuilder

Pure service that composes the Session_Preamble SAS code block.

```csharp
public sealed class PreambleBuilder
{
    /// <summary>
    /// Builds the Session_Preamble block. Pure function — no I/O.
    /// </summary>
    public string Build(
        string userId,
        string sessionId,
        IReadOnlyDictionary<string, string> macroVars)
    {
        var sb = new StringBuilder();
        sb.AppendLine($"""LIBNAME SESSLIB "/sas/sessions/{userId}/{sessionId}/";""");
        sb.AppendLine($"%let SESSIONID = {sessionId};");
        foreach (var (name, value) in macroVars)
            sb.AppendLine($"%let {name} = {value};");
        return sb.ToString();
    }
}
```

### LogParserService

Pure service that parses `%put _user_;` log output and classifies log lines.

```csharp
public sealed class LogParserService
{
    // Regex matching lines like:  MYVAR=hello world
    private static readonly Regex UserVarRegex =
        new(@"^([A-Z_][A-Z0-9_]*)=(.*)$", RegexOptions.Compiled | RegexOptions.Multiline);

    /// <summary>
    /// Scans log lines for the %put _user_; output block.
    /// Returns all parsed name-value pairs (may be empty).
    /// </summary>
    public Dictionary<string, string> ParseUserMacroVars(IEnumerable<string> logLines)
    {
        var result = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        var inBlock = false;
        foreach (var line in logLines)
        {
            if (line.Contains("MPRINT") || line.Contains("MLOGIC")) continue;
            if (line.TrimStart().StartsWith("SESSIONID=", StringComparison.OrdinalIgnoreCase))
                inBlock = true;
            if (!inBlock) continue;
            var m = UserVarRegex.Match(line.TrimStart());
            if (m.Success)
                result[m.Groups[1].Value] = m.Groups[2].Value.TrimEnd();
            else if (inBlock && !string.IsNullOrWhiteSpace(line))
                inBlock = false; // end of _user_ block
        }
        return result;
    }

    /// <summary>Classifies a raw log line by its severity prefix.</summary>
    public static LogLine Classify(string raw) => LogLine.Parse(raw);

    /// <summary>
    /// Returns the first ERROR or WARNING line text, or "Completed" if none.
    /// </summary>
    public static string Summarize(IEnumerable<string> logLines)
    {
        foreach (var line in logLines)
        {
            var t = line.TrimStart();
            if (t.StartsWith("ERROR",   StringComparison.OrdinalIgnoreCase)) return line;
            if (t.StartsWith("WARNING", StringComparison.OrdinalIgnoreCase)) return line;
        }
        return "Completed";
    }
}
```

### LogStreamingHub

```csharp
public sealed class LogStreamingHub : Hub
{
    /// <summary>Subscribe to log lines for a specific job.</summary>
    public async Task JoinJob(string jobId) =>
        await Groups.AddToGroupAsync(Context.ConnectionId, jobId);

    /// <summary>Unsubscribe from a job's log stream.</summary>
    public async Task LeaveJob(string jobId) =>
        await Groups.RemoveFromGroupAsync(Context.ConnectionId, jobId);
}
```

### SseLogController

```csharp
[ApiController]
[Route("api/session-jobs")]
public sealed class SseLogController(
    ISlcHubClient hubClient,
    ILogger<SseLogController> logger) : ControllerBase
{
    [HttpGet("{jobId}/log-stream")]
    public async Task StreamLog(string jobId, CancellationToken ct)
    {
        Response.ContentType = "text/event-stream";
        Response.Headers.CacheControl = "no-cache";
        await foreach (var line in hubClient.StreamLogAsync(jobId, ct))
        {
            await Response.WriteAsync($"data: {line}\n\n", ct);
            await Response.Body.FlushAsync(ct);
        }
    }
}
```

### SessionJobApiController

```csharp
[ApiController]
[Route("api/session-jobs")]
public sealed class SessionJobApiController(
    ISessionJobOrchestrator orchestrator,
    ILogger<SessionJobApiController> logger) : ControllerBase
{
    [HttpPost]
    public async Task<IActionResult> SubmitJob(
        [FromBody] JobSubmitRequest request,
        CancellationToken ct)
    {
        try
        {
            var userId = HttpContext.Session.GetString("UserId")
                ?? return BadRequest("UserId not in session.");
            var jobId  = await orchestrator.SubmitAsync(
                userId, request.SessionId, request.SourceCode, ct);
            return Ok(new JobSubmitResponse(jobId));
        }
        catch (SlcHubException ex)
        {
            logger.LogWarning(ex, "SLC Hub returned non-success.");
            return StatusCode(502, ex.Message);
        }
    }
}
```

### FilesApiController

```csharp
[ApiController]
[Route("api/files")]
public sealed class FilesApiController(
    ISessionJobOrchestrator orchestrator,
    ILogger<FilesApiController> logger) : ControllerBase
{
    /// <summary>
    /// Lists all .sas7bdat files in the session working directory.
    /// </summary>
    [HttpGet]
    public IActionResult ListFiles()
    {
        var userId = HttpContext.Session.GetString("UserId");
        var sessionId = HttpContext.Session.GetString("SessionId");
        if (userId is null || sessionId is null)
            return BadRequest("UserId or SessionId not in session.");

        var sessionPath = $"/sas/sessions/{userId}/{sessionId}/";
        if (!Directory.Exists(sessionPath))
            return Ok(new { files = Array.Empty<DatasetFileInfo>() });

        var files = Directory.GetFiles(sessionPath, "*.sas7bdat")
            .Select(path =>
            {
                var fileInfo = new FileInfo(path);
                return new DatasetFileInfo(
                    Path.GetFileNameWithoutExtension(fileInfo.Name),
                    fileInfo.Length,
                    fileInfo.LastWriteTimeUtc);
            })
            .OrderBy(f => f.Name)
            .ToList();

        return Ok(new { files });
    }

    /// <summary>
    /// Submits a background job to introspect a dataset and returns the jobId.
    /// The caller should subscribe to SignalR to receive the dataset contents.
    /// </summary>
    [HttpPost("{fileName}/view")]
    public async Task<IActionResult> ViewDataset(
        string fileName,
        CancellationToken ct)
    {
        try
        {
            var userId = HttpContext.Session.GetString("UserId")
                ?? return BadRequest("UserId not in session.");
            var sessionId = HttpContext.Session.GetString("SessionId")
                ?? return BadRequest("SessionId not in session.");

            // Generate SAS code to introspect the dataset
            var sasCode = $@"
PROC CONTENTS DATA=SESSLIB.{fileName} SHORT;
RUN;

PROC PRINT DATA=SESSLIB.{fileName}(OBS=1000);
RUN;
";

            var jobId = await orchestrator.SubmitAsync(userId, sessionId, sasCode, ct);
            return Ok(new { jobId });
        }
        catch (SlcHubException ex)
        {
            logger.LogWarning(ex, "SLC Hub returned non-success.");
            return StatusCode(502, ex.Message);
        }
    }
}

public record DatasetFileInfo(string Name, long SizeBytes, DateTime LastModified);
```

### MacroVarStore

```csharp
public interface IMacroVarStore
{
    Task<IReadOnlyDictionary<string, string>> GetAsync(string sessionId);
    Task SetAsync(string sessionId, IReadOnlyDictionary<string, string> vars);
    Task SetVarAsync(string sessionId, string name, string value);
}

public sealed class MacroVarStore : IMacroVarStore
{
    private readonly ConcurrentDictionary<string, Dictionary<string, string>> _store = new();

    public Task<IReadOnlyDictionary<string, string>> GetAsync(string sessionId)
    {
        var vars = _store.TryGetValue(sessionId, out var v)
            ? (IReadOnlyDictionary<string, string>)v
            : new Dictionary<string, string>();
        return Task.FromResult(vars);
    }

    public Task SetAsync(string sessionId, IReadOnlyDictionary<string, string> vars)
    {
        _store[sessionId] = new Dictionary<string, string>(vars);
        return Task.CompletedTask;
    }

    public Task SetVarAsync(string sessionId, string name, string value)
    {
        _store.AddOrUpdate(sessionId,
            _ => new Dictionary<string, string> { [name] = value },
            (_, existing) => { existing[name] = value; return existing; });
        return Task.CompletedTask;
    }
}
```

### ProgramHistoryStore

```csharp
public interface IProgramHistoryStore
{
    Task AddAsync(ProgramHistoryRecord record);
    /// <summary>Returns records for the given user, sorted by SubmittedAt descending.</summary>
    Task<IReadOnlyList<ProgramHistoryRecord>> GetByUserAsync(string userId);
}

public sealed class ProgramHistoryStore : IProgramHistoryStore
{
    private readonly ConcurrentDictionary<string, List<ProgramHistoryRecord>> _store = new();

    public Task AddAsync(ProgramHistoryRecord record)
    {
        _store.AddOrUpdate(record.UserId,
            _ => [record],
            (_, list) => { lock (list) { list.Add(record); } return list; });
        return Task.CompletedTask;
    }

    public Task<IReadOnlyList<ProgramHistoryRecord>> GetByUserAsync(string userId)
    {
        if (!_store.TryGetValue(userId, out var list))
            return Task.FromResult<IReadOnlyList<ProgramHistoryRecord>>([]);
        IReadOnlyList<ProgramHistoryRecord> sorted;
        lock (list)
            sorted = [.. list.OrderByDescending(r => r.SubmittedAt)];
        return Task.FromResult(sorted);
    }
}
```

### BearerTokenRequiredFilter

```csharp
public sealed class BearerTokenRequiredFilter(ITokenManager tokenManager) : IAsyncActionFilter
{
    public async Task OnActionExecutionAsync(ActionExecutingContext context, ActionExecutionDelegate next)
    {
        try
        {
            await tokenManager.EnsureValidTokenAsync(context.HttpContext.RequestAborted);
            await next();
        }
        catch (InvalidOperationException ex)
        {
            context.Result = new StatusCodeResult(503); // Service unavailable
        }
    }
}
```

### Dependency Injection Registration (Program.cs)

```csharp
// Validate required configuration
var requiredKeys = new[] {
    "SlcHub:ServiceAccount:Username",
    "SlcHub:ServiceAccount:Password",
    "SlcHub:UserId",
    "SlcHub:BaseUrl",
    "SlcHub:Namespace",
    "SlcHub:ExecutionProfile"
};
foreach (var key in requiredKeys)
{
    if (string.IsNullOrEmpty(builder.Configuration[key]))
        throw new InvalidOperationException($"Required configuration key '{key}' is missing or empty.");
}

// Session
builder.Services.AddDistributedMemoryCache();
builder.Services.AddSession(options =>
{
    options.IdleTimeout = TimeSpan.FromHours(8);
    options.Cookie.HttpOnly = true;
    options.Cookie.IsEssential = true;
    options.Cookie.SecurePolicy = CookieSecurePolicy.Always;
});
builder.Services.AddHttpContextAccessor();

// Typed HttpClient
builder.Services.AddHttpClient<ISlcHubClient, SlcHubClient>(client =>
    client.BaseAddress = new Uri(builder.Configuration["SlcHub:BaseUrl"]!));

// Singleton TokenManager (shared across requests)
builder.Services.AddSingleton<ITokenManager, TokenManager>();

// Scoped services (per request)
builder.Services.AddScoped<ISessionJobOrchestrator, SessionJobOrchestrator>();
builder.Services.AddScoped<PreambleBuilder>();
builder.Services.AddScoped<LogParserService>();

// Singleton stores (process lifetime)
builder.Services.AddSingleton<IMacroVarStore, MacroVarStore>();
builder.Services.AddSingleton<IProgramHistoryStore, ProgramHistoryStore>();

// SignalR
builder.Services.AddSignalR();

// MVC with global bearer filter
builder.Services.AddControllersWithViews(options =>
    options.Filters.Add<BearerTokenRequiredFilter>());
```

### Configuration (appsettings.json)

```json
{
  "Logging": {
    "LogLevel": {
      "Default": "Information",
      "Microsoft.AspNetCore": "Warning"
    }
  },
  "AllowedHosts": "*",
  "SlcHub": {
    "BaseUrl": "https://your-slc-hub-server:port",
    "ServiceAccount": {
      "Username": "service-account-username",
      "Password": "service-account-password"
    },
    "UserId": "user-to-impersonate",
    "Namespace": "your-namespace",
    "ExecutionProfile": "your-execution-profile"
  }
}
```

```javascript
// wwwroot/js/log-viewer.js
const connection = new signalR.HubConnectionBuilder()
    .withUrl("/hubs/log")
    .withAutomaticReconnect()
    .build();

connection.on("ReceiveLog", appendLine);
connection.on("JobComplete", () => markJobDone());

connection.start()
    .then(() => connection.invoke("JoinJob", jobId))
    .catch(() => {
        // SignalR unavailable — fall back to SSE
        const evtSource = new EventSource(`/api/session-jobs/${jobId}/log-stream`);
        evtSource.onmessage = e => appendLine(e.data);
    });

function appendLine(text) {
    const div = document.createElement("div");
    div.textContent = text;
    const t = text.trimStart().toUpperCase();
    if      (t.startsWith("ERROR"))   div.className = "log-error";
    else if (t.startsWith("WARNING")) div.className = "log-warning";
    else if (t.startsWith("NOTE"))    div.className = "log-note";
    logContainer.appendChild(div);
    logContainer.scrollTop = logContainer.scrollHeight; // auto-scroll
    updateJumpToError();
}
```

```javascript
// wwwroot/js/file-browser.js
// Manages the Files tab and Dataset Viewer modal

async function refreshFileList() {
    try {
        const response = await fetch('/api/files');
        const data = await response.json();
        renderFileList(data.files);
    } catch (error) {
        console.error('Failed to load file list:', error);
    }
}

function renderFileList(files) {
    const container = document.getElementById('files-list');
    container.innerHTML = '';
    
    if (files.length === 0) {
        container.innerHTML = '<p class="no-files">No dataset files found</p>';
        return;
    }
    
    files.forEach(file => {
        const item = document.createElement('div');
        item.className = 'file-item';
        item.innerHTML = `
            <span class="file-name">${file.name}.sas7bdat</span>
            <span class="file-size">${formatBytes(file.sizeBytes)}</span>
            <span class="file-date">${formatDate(file.lastModified)}</span>
        `;
        item.onclick = () => viewDataset(file.name);
        container.appendChild(item);
    });
}

async function viewDataset(fileName) {
    showModal('Loading dataset...');
    
    try {
        // Submit job to introspect the dataset
        const response = await fetch(`/api/files/${fileName}/view`, { method: 'POST' });
        const data = await response.json();
        const jobId = data.jobId;
        
        // Subscribe to SignalR for this job
        const datasetConnection = new signalR.HubConnectionBuilder()
            .withUrl("/hubs/log")
            .build();
        
        let logOutput = '';
        datasetConnection.on("ReceiveLog", line => { logOutput += line + '\n'; });
        datasetConnection.on("JobComplete", () => {
            parseAndDisplayDataset(fileName, logOutput);
            datasetConnection.stop();
        });
        
        await datasetConnection.start();
        await datasetConnection.invoke("JoinJob", jobId);
    } catch (error) {
        showModal(`Error loading dataset: ${error.message}`);
    }
}

function parseAndDisplayDataset(fileName, logOutput) {
    // Parse PROC CONTENTS and PROC PRINT output
    const lines = logOutput.split('\n');
    // TODO: Implement SAS output parsing logic
    // Extract column names from PROC CONTENTS SHORT output
    // Extract rows from PROC PRINT output
    
    showModal(`
        <div class="dataset-viewer">
            <h3>${fileName}.sas7bdat</h3>
            <div class="dataset-table-container">
                <table id="dataset-table" class="dataset-table">
                    <!-- Populated by parsing logic -->
                </table>
            </div>
            <div class="dataset-pagination">
                <button onclick="prevPage()">Previous</button>
                <span id="page-info">Page 1</span>
                <button onclick="nextPage()">Next</button>
            </div>
        </div>
    `);
}

function formatBytes(bytes) {
    if (bytes < 1024) return bytes + ' B';
    if (bytes < 1024 * 1024) return (bytes / 1024).toFixed(1) + ' KB';
    return (bytes / (1024 * 1024)).toFixed(1) + ' MB';
}

function formatDate(isoString) {
    return new Date(isoString).toLocaleString();
}

// Refresh file list after job completes
connection.on("JobComplete", () => {
    setTimeout(refreshFileList, 500); // Delay to allow file system sync
});
```

---

## Data Models

```csharp
// Models/SessionInfo.cs
public record SessionInfo(
    string UserId,
    string SessionId,        // UUID string
    DateTime CreatedAt,
    DateTime LastAccessedAt
);

// Models/JobSubmitRequest.cs
public record JobSubmitRequest(
    string SessionId,
    string SourceCode
);

// Models/JobSubmitResponse.cs
public record JobSubmitResponse(
    string JobId
);

// Models/LogLine.cs
public enum LogSeverity { Plain, Note, Warning, Error }

public record LogLine(string Text, LogSeverity Severity)
{
    // Classifies a raw string by its SAS log prefix
    public static LogLine Parse(string raw) => raw.TrimStart() switch
    {
        var s when s.StartsWith("ERROR",   StringComparison.OrdinalIgnoreCase) => new(raw, LogSeverity.Error),
        var s when s.StartsWith("WARNING", StringComparison.OrdinalIgnoreCase) => new(raw, LogSeverity.Warning),
        var s when s.StartsWith("NOTE",    StringComparison.OrdinalIgnoreCase) => new(raw, LogSeverity.Note),
        _ => new(raw, LogSeverity.Plain)
    };
}

// Models/MacroVar.cs
public record MacroVar(string Name, string Value);

// Models/ProgramHistoryRecord.cs
public record ProgramHistoryRecord(
    string RecordId,                         // UUID
    string UserId,
    string SessionId,
    DateTime SubmittedAt,
    string SourceCode,                       // user-supplied portion only
    string LogSummary,                       // first ERROR/WARNING, or "Completed"
    IReadOnlyList<string> DatasetsProduced
);

// Models/DatasetRow.cs
public record DatasetRow(IReadOnlyDictionary<string, string> Columns);

// Models/JobResultFile.cs
public record JobResultFile(string Name, string Url);

// Models/DatasetFileInfo.cs
public record DatasetFileInfo(string Name, long SizeBytes, DateTime LastModified);

// Models/PagedResult.cs
public record PagedResult<T>(
    IReadOnlyList<T> Items,
    int TotalCount,
    int Page,
    int PageSize
);
```

---

## Error Handling

### SlcHubException

`SlcHubClient` throws `SlcHubException` (carrying the Hub's HTTP status code and response body) for any non-2xx response. `SessionJobApiController` catches this and returns HTTP 502 with the Hub error message, ensuring the browser never receives a raw 5xx from the Hub.

### Bearer Token Absent or Expired

`BearerTokenRequiredFilter` intercepts all MVC and API actions and calls `TokenManager.EnsureValidTokenAsync` before the action executes. If the token is absent or expired, `TokenManager` automatically acquires a new token via the service account login + impersonate flow, then allows the request to proceed. If authentication fails, the filter returns HTTP 503 (Service Unavailable). `SlcHubClient.ApplyBearerToken` additionally throws `InvalidOperationException` as a defense-in-depth measure for any path that bypasses the filter.

### Log Streaming Errors

`StreamAndFinalizeAsync` wraps the entire log streaming loop in a try/catch. On exception it sends a `"JobError"` SignalR message to all subscribers so the UI can display an error banner rather than silently hanging.

### Session Folder Creation

`SessionController.New` calls `Directory.CreateDirectory` which is idempotent and does not throw if the folder already exists. If the path is inaccessible (permissions), the exception propagates as a 500; no silent data loss occurs.

### Validation

- `POST /api/session-jobs` rejects requests where `SessionId` or `SourceCode` is null/empty with HTTP 400 (model validation via `[Required]` attributes on `JobSubmitRequest`).
- Macro variable names in `%let` generation are validated against the SAS identifier pattern `[A-Z_][A-Z0-9_]*` before code assembly to prevent injection.

---

## Testing Strategy

### Unit Tests

Focus on pure services that have deterministic, data-varying behavior:

- **`PreambleBuilder`** — verify `LIBNAME SESSLIB`, `%let SESSIONID`, and one `%let` per macro var appear in the output; verify user code and trailer are positioned correctly.
- **`LogParserService`** — verify `ParseUserMacroVars` extracts all name-value pairs from representative `%put _user_;` log blocks; verify `Classify` assigns the correct severity for all four prefixes; verify `Summarize` returns the first ERROR/WARNING line or "Completed".
- **`LogLine.Parse`** — parameterized tests covering ERROR, WARNING, NOTE, mixed-case variants, and plain lines.
- **`MacroVarStore`** and **`ProgramHistoryStore`** — Get/Set round-trips, isolation between different SessionIds/UserIds, ordering invariant for history.
- **`SessionJobApiController`** — mock orchestrator; verify 200 on success, 502 on `SlcHubException`.

### Property-Based Tests (xUnit + FsCheck or CsCheck)

- **`PreambleBuilder.Build`** — For any `userId`, `sessionId`, and arbitrary `Dictionary<string, string>` of macro vars, the output always contains `LIBNAME SESSLIB`, `%let SESSIONID`, and exactly one `%let` per dictionary entry.
- **`LogLine.Parse`** — For any string, Parse returns one of four exact `LogSeverity` values; for any string starting with `ERROR`/`WARNING`/`NOTE` (case-insensitive), severity matches the prefix.
- **`MacroVarStore` round-trip** — For any `sessionId` and any `Dictionary<string, string>`, `SetAsync` then `GetAsync` returns an equal dictionary.
- **`ProgramHistoryStore` ordering** — For any list of `ProgramHistoryRecord` objects inserted in arbitrary order, `GetByUserAsync` returns them in descending `SubmittedAt` order.
- **`LogParserService.ParseUserMacroVars` round-trip** — For any dictionary of macro var pairs, rendering them as `%put _user_;` output and then parsing recovers the original dictionary.
- **Pagination** — For any `N ≥ 0` rows, `PagedResult` pages cover all rows with no duplicates and each page ≤ 100 rows.
- **Sort invariant** — For any `IEnumerable<DatasetRow>` and any column key, ascending sort produces a non-decreasing sequence; descending sort produces a non-increasing sequence.

### Integration / Smoke Tests

- Verify `/hubs/log` is mapped and returns a valid WebSocket upgrade response.
- Verify `GET /api/session-jobs/{jobId}/log-stream` returns `Content-Type: text/event-stream`.
- Verify all MVC routes return HTTP 200 (with a seeded session cookie).
- Verify `POST /api/session-jobs` returns HTTP 400 for missing fields and HTTP 502 when the Hub mock returns a 503.

---

## Correctness Properties

*A property is a characteristic or behavior that should hold true across all valid executions of a system — essentially, a formal statement about what the system should do. Properties serve as the bridge between human-readable specifications and machine-verifiable correctness guarantees.*

### Property 1: Bearer Token Is Always Attached to Hub Requests

*For any* outbound HTTP request made by `SlcHubClient` — regardless of the target endpoint or HTTP method — the request MUST carry an `Authorization: Bearer {token}` header whose value equals the token stored in the current user's ASP.NET Session.

**Validates: Requirements 1.2**

---

### Property 2: New Session Always Produces a Valid UUID and Folder

*For any* call to create a new session for any `UserId`, the returned `SessionId` MUST be a well-formed UUID (RFC 4122) and the folder at `/sas/sessions/{UserId}/{SessionId}/` MUST exist after the call completes.

**Validates: Requirements 2.1**

---

### Property 3: Session Listing Is Always in Descending Timestamp Order

*For any* non-empty collection of sessions for any `UserId`, the list returned by the session listing function MUST satisfy `sessions[i].LastAccessedAt >= sessions[i+1].LastAccessedAt` for every consecutive pair.

**Validates: Requirements 2.3**

---

### Property 4: Every Job Submission Carries the Active SessionId

*For any* job submission, the `SessionId` field of the assembled request forwarded to `SlcHubClient` MUST equal the `ActiveSessionId` stored in the submitting user's ASP.NET Session at the time of submission.

**Validates: Requirements 2.4**

---

### Property 5: Preamble Structure Invariant

*For any* user-supplied SAS source string, `UserId`, `SessionId`, and macro variable map, the full assembled program produced by `SessionJobOrchestrator` MUST (in order): begin with a `LIBNAME SESSLIB "/sas/sessions/{UserId}/{SessionId}/";` line, include a `%let SESSIONID = {SessionId};` line, include exactly one `%let {name} = {value};` line per entry in the macro variable map, then contain the user-supplied code verbatim, then end with the line `%put _user_;`.

**Validates: Requirements 3.1, 3.2, 3.3, 3.4, 3.5**

---

### Property 6: Log Line Classification Is Total and Deterministic

*For any* raw log line string, `LogLine.Parse` MUST return exactly one of `{Error, Warning, Note, Plain}` and MUST always return the same severity for the same input. Specifically: lines whose trimmed text begins with `ERROR` (case-insensitive) → `Error`; `WARNING` → `Warning`; `NOTE` → `Note`; all others → `Plain`.

**Validates: Requirements 6.1**

---

### Property 7: Every Log Line Is Forwarded to SignalR

*For any* sequence of log line strings produced by `SlcHubClient.StreamLogAsync` for any `jobId`, every line in that sequence MUST be forwarded — in the same order — as a `"ReceiveLog"` SignalR message to the group named `jobId`.

**Validates: Requirements 5.2, 5.3**

---

### Property 8: Dataset Inspection Job Contains Required PROC Statements

*For any* valid SAS dataset name string, the SAS code generated for the dataset introspection job MUST contain both a `PROC CONTENTS` statement and a `PROC PRINT DATA={dataset}(OBS=100)` statement referencing that exact dataset name.

**Validates: Requirements 7.1**

---

### Property 9: Dataset Pagination Invariant

*For any* collection of `N` dataset rows (N ≥ 0) paginated at 100 rows per page: the total number of pages MUST equal `⌈N/100⌉` (0 when N = 0); each page except possibly the last MUST contain exactly 100 rows; the last page MUST contain between 1 and 100 rows; no row MAY appear on more than one page.

**Validates: Requirements 7.4**

---

### Property 10: Dataset Sort Order Invariant

*For any* collection of `DatasetRow` objects and any column name present in those rows, ascending sort MUST produce a non-decreasing sequence of values in that column; descending sort MUST produce a non-increasing sequence.

**Validates: Requirements 7.3**

---

### Property 11: Macro Variable Parsing Round-Trip

*For any* dictionary of macro variable name–value pairs that constitutes valid SAS `%put _user_;` output, rendering those pairs as output lines and then parsing with `LogParserService.ParseUserMacroVars` MUST recover a dictionary equal to the original.

**Validates: Requirements 8.1**

---

### Property 12: %let Assignment Code Generation

*For any* valid SAS macro variable name and any string value, the code generated for an inline edit submission MUST be exactly `%let {name} = {value};` (single statement, no extra lines).

**Validates: Requirements 8.3**

---

### Property 13: ProgramHistory Record Completeness

*For any* completed job for any `UserId` and `SessionId`, the `ProgramHistoryRecord` persisted to `ProgramHistoryStore` MUST contain: a non-null `RecordId`, the correct `UserId`, the correct `SessionId`, a `SubmittedAt` timestamp within the job's execution window, the user-supplied source code (not the assembled code), a non-null `LogSummary`, and the list of datasets produced.

**Validates: Requirements 9.1**

---

### Property 14: ProgramHistory Is Always Returned in Descending Timestamp Order

*For any* non-empty collection of `ProgramHistoryRecord` objects for any `UserId`, the list returned by `IProgramHistoryStore.GetByUserAsync` MUST satisfy `records[i].SubmittedAt >= records[i+1].SubmittedAt` for every consecutive pair.

**Validates: Requirements 9.2**

---

### Property 15: History Preview Never Exceeds 120 Characters

*For any* `ProgramHistoryRecord` whose `SourceCode` length exceeds 120 characters, the code preview displayed in the History view MUST have a character length of exactly 120 (before any ellipsis suffix).

**Validates: Requirements 9.3**

---

### Property 16: MacroVarStore Round-Trip Consistency

*For any* `SessionId` and any dictionary of macro variable name–value pairs, calling `IMacroVarStore.SetAsync` then `IMacroVarStore.GetAsync` with the same `SessionId` MUST return a dictionary equal to the one that was stored (same keys, same values, no extra or missing entries).

**Validates: Requirements 12.1, 12.3**

---

### Property 17: ProgramHistoryStore Retrieval Completeness

*For any* `UserId` and any sequence of `ProgramHistoryRecord` objects added via `IProgramHistoryStore.AddAsync`, calling `GetByUserAsync` for that `UserId` MUST return a list containing all added records with no record silently dropped.

**Validates: Requirements 12.2, 12.3**
