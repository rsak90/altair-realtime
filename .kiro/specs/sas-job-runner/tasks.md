# Implementation Plan: sas-job-runner

## Overview

Scaffold a greenfield ASP.NET Core MVC (.NET 10) application from scratch. Work proceeds in layers: project setup → data models → service interfaces and in-memory stores → core services (PreambleBuilder, LogParserService) → HTTP client (SlcHubClient) → orchestration (SessionJobOrchestrator) → SignalR hub and SSE controller → MVC controllers and view-models → Razor views and shared layout → client-side JavaScript → DI wiring in Program.cs → unit and property-based tests.

---

## Tasks

- [x] 1. Scaffold project structure and configuration
  - Run `dotnet new mvc -n SasJobRunner --framework net10.0` and verify the project builds
  - Add NuGet packages: `Microsoft.AspNetCore.SignalR`, `FsCheck.Xunit` (or `CsCheck`), `xunit`, `Microsoft.NET.Test.Sdk`, `coverlet.collector`
  - Create `appsettings.json` with a placeholder `SlcHub:BaseUrl` key
  - Create `appsettings.Development.json` with a local dev override
  - Create directory stubs: `Controllers/Api/`, `Hubs/`, `Services/`, `Models/`, `ViewModels/`, `Views/Shared/`, `Views/Editor/`, `Views/Session/`, `Views/Dataset/`, `Views/History/`, `wwwroot/css/`, `wwwroot/js/`
  - Create the config file at `.kiro/specs/sas-job-runner/.config.kiro`
  - _Requirements: 11.1_

- [x] 2. Define data models
  - [x] 2.1 Create all record types in `Models/`
    - `SessionInfo`, `JobSubmitRequest` (with `[Required]` attributes), `JobSubmitResponse`, `LogLine` (with `LogSeverity` enum and `Parse` static method), `MacroVar`, `ProgramHistoryRecord`, `DatasetRow`, `PagedResult<T>`
    - _Requirements: 3.1, 4.1, 6.1, 7.4, 8.1, 9.1_

  - [ ]* 2.2 Write property test: LogLine.Parse is total and deterministic (Property 6)
    - **Property 6: Log Line Classification Is Total and Deterministic**
    - For any raw string, `LogLine.Parse` must return one of the four severity values and always return the same severity for the same input; lines trimmed-starting with ERROR/WARNING/NOTE must map to the matching severity
    - **Validates: Requirements 6.1**

- [x] 3. Implement in-memory stores
  - [x] 3.1 Implement `IMacroVarStore` interface and `MacroVarStore` class
    - `ConcurrentDictionary`-backed, `GetAsync`, `SetAsync`, `SetVarAsync` as per design
    - _Requirements: 12.1, 12.3_

  - [x] 3.2 Implement `IProgramHistoryStore` interface and `ProgramHistoryStore` class
    - `ConcurrentDictionary`-backed, `AddAsync`, `GetByUserAsync` returning descending `SubmittedAt` order
    - _Requirements: 12.2, 12.3_

  - [ ]* 3.3 Write property test: MacroVarStore round-trip (Property 16)
    - **Property 16: MacroVarStore Round-Trip Consistency**
    - For any `sessionId` and any `Dictionary<string, string>`, `SetAsync` then `GetAsync` returns an equal dictionary
    - **Validates: Requirements 12.1, 12.3**

  - [ ]* 3.4 Write property test: ProgramHistoryStore ordering (Property 14)
    - **Property 14: ProgramHistory Is Always Returned in Descending Timestamp Order**
    - For any list of records added in arbitrary order, `GetByUserAsync` satisfies `records[i].SubmittedAt >= records[i+1].SubmittedAt` for all consecutive pairs
    - **Validates: Requirements 9.2**

  - [ ]* 3.5 Write property test: ProgramHistoryStore completeness (Property 17)
    - **Property 17: ProgramHistoryStore Retrieval Completeness**
    - For any sequence of records added via `AddAsync`, `GetByUserAsync` returns a list containing all added records
    - **Validates: Requirements 12.2, 12.3**

- [x] 4. Implement `PreambleBuilder`
  - [x] 4.1 Implement `PreambleBuilder.Build` in `Services/PreambleBuilder.cs`
    - Emits `LIBNAME SESSLIB "/sas/sessions/{userId}/{sessionId}/";`, `%let SESSIONID = {sessionId};`, and one `%let {name} = {value};` per macro var entry
    - _Requirements: 3.1, 3.2, 3.3, 3.4_

  - [ ]* 4.2 Write property test: Preamble structure invariant (Property 5)
    - **Property 5: Preamble Structure Invariant**
    - For any `userId`, `sessionId`, and arbitrary `Dictionary<string, string>` of macro vars, the output always contains `LIBNAME SESSLIB`, `%let SESSIONID`, and exactly one `%let` per dictionary entry
    - **Validates: Requirements 3.1, 3.2, 3.3, 3.4, 3.5**

- [x] 5. Implement `LogParserService`
  - [x] 5.1 Implement `LogParserService` in `Services/LogParserService.cs`
    - `ParseUserMacroVars` scanning `%put _user_;` output blocks with `UserVarRegex`
    - Static `Classify` (delegates to `LogLine.Parse`) and static `Summarize` methods
    - _Requirements: 6.1, 8.1, 9.1_

  - [ ]* 5.2 Write property test: macro var round-trip (Property 11)
    - **Property 11: Macro Variable Parsing Round-Trip**
    - For any dictionary of valid macro var name–value pairs, rendering as `%put _user_;` output and parsing recovers the original dictionary
    - **Validates: Requirements 8.1**

- [x] 6. Implement `SlcHubClient`
  - [x] 6.1 Define `ISlcHubClient` interface and `SlcHubClient` typed-HttpClient implementation in `Services/`
    - `ApplyBearerToken` reads from `IHttpContextAccessor → Session["BearerToken"]`; `PostJobAsync` posts to `/jobs`; `StreamLogAsync` streams from `/jobs/{jobId}/log-stream` using `HttpCompletionOption.ResponseHeadersRead`
    - Define `SlcHubException(string message, int statusCode)`
    - _Requirements: 1.2, 1.3, 4.3, 4.5, 5.3_

  - [ ]* 6.2 Write property test: Bearer token always attached (Property 1)
    - **Property 1: Bearer Token Is Always Attached to Hub Requests**
    - For any outbound call via `SlcHubClient`, the request must carry `Authorization: Bearer {token}` matching the session value; use a `DelegatingHandler` test double to capture request headers
    - **Validates: Requirements 1.2**

- [x] 7. Implement `LogStreamingHub` and `SseLogController`
  - [x] 7.1 Implement `LogStreamingHub` in `Hubs/LogStreamingHub.cs`
    - `JoinJob(string jobId)` / `LeaveJob(string jobId)` using SignalR group management
    - _Requirements: 5.1, 5.2_

  - [x] 7.2 Implement `SseLogController` in `Controllers/Api/SseLogController.cs`
    - Sets `Content-Type: text/event-stream`, streams via `hubClient.StreamLogAsync`, writes `data: {line}\n\n` per line
    - _Requirements: 5.4, 5.5_

- [x] 8. Implement `SessionJobOrchestrator`
  - [x] 8.1 Implement `ISessionJobOrchestrator` interface and `SessionJobOrchestrator` in `Services/`
    - `SubmitAsync`: fetch macro vars → build preamble → assemble full code → `PostJobAsync` → fire-and-forget `StreamAndFinalizeAsync`
    - `StreamAndFinalizeAsync`: stream log lines → push each via `IHubContext<LogStreamingHub>` → parse macro vars → update store → persist history record → send `JobComplete` or `JobError`
    - `ExtractDatasets` parses `NOTE: The data set SESSLIB.{name}` lines
    - _Requirements: 3.1, 3.5, 4.2, 4.3, 5.2, 5.3, 8.1, 8.4, 9.1_

  - [ ]* 8.2 Write property test: Every log line forwarded to SignalR (Property 7)
    - **Property 7: Every Log Line Is Forwarded to SignalR**
    - For any sequence of raw log lines from a mock `ISlcHubClient`, every line must appear exactly once as a `"ReceiveLog"` message to the correct group, in order
    - **Validates: Requirements 5.2, 5.3**

  - [ ]* 8.3 Write property test: ProgramHistory record completeness (Property 13)
    - **Property 13: ProgramHistory Record Completeness**
    - For any completed job, the persisted `ProgramHistoryRecord` must have non-null `RecordId`, correct `UserId`/`SessionId`, `SubmittedAt` within execution window, user source code (not assembled), non-null `LogSummary`, and datasets list
    - **Validates: Requirements 9.1**

- [x] 9. Implement `BearerTokenRequiredFilter` and API controllers
  - [x] 9.1 Implement `BearerTokenRequiredFilter` in `Filters/BearerTokenRequiredFilter.cs`
    - `IActionFilter.OnActionExecuting` redirects to `Auth/Login` when `Session["BearerToken"]` is null or empty
    - _Requirements: 1.3_

  - [x] 9.2 Implement `SessionJobApiController` in `Controllers/Api/SessionJobApiController.cs`
    - `POST /api/session-jobs` reads `UserId` from session, delegates to orchestrator, returns 200 `{jobId}` or 400/502
    - _Requirements: 4.1, 4.2, 4.4, 4.5_

  - [x] 9.3 Add stub `AuthController` with a `Login` GET action (renders a simple token-entry form) and a `Login` POST action that stores `BearerToken` and `UserId` in session
    - _Requirements: 1.1_
    - **Note: This stub controller should be REMOVED after task 20.1 is complete, as authentication is now fully automatic via TokenManager**

- [x] 10. Implement MVC controllers and view-models
  - [x] 10.1 Implement `HomeController` (redirect to `/Editor`), `EditorController` (GET `/Editor`), `SessionController` (GET + POST `/Session/New`, POST `/Session/Resume`), `DatasetController` (GET `/Dataset`), `HistoryController` (GET `/History`)
    - `SessionController.New` generates UUID, calls `Directory.CreateDirectory`, stores `SessionId` in session
    - `SessionController.Resume` loads macro vars from store and stores `SessionId` in session
    - _Requirements: 2.1, 2.2, 2.3, 2.4, 11.1_

  - [x] 10.2 Create view-model classes in `ViewModels/`: `EditorViewModel`, `SessionViewModel`, `DatasetViewModel`, `HistoryViewModel`
    - `HistoryViewModel` includes truncation to 120 characters for `SourceCode` preview
    - _Requirements: 9.3, 11.1_

  - [ ]* 10.3 Write property test: History preview never exceeds 120 characters (Property 15)
    - **Property 15: History Preview Never Exceeds 120 Characters**
    - For any `ProgramHistoryRecord` with `SourceCode.Length > 120`, the preview field in `HistoryViewModel` must have length exactly 120 (before any ellipsis)
    - **Validates: Requirements 9.3**

  - [ ]* 10.4 Write property test: Dataset inspection job contains required PROC statements (Property 8)
    - **Property 8: Dataset Inspection Job Contains Required PROC Statements**
    - For any valid SAS dataset name string, the generated SAS code must contain both `PROC CONTENTS` and `PROC PRINT DATA={dataset}(OBS=100)` referencing that exact dataset name
    - **Validates: Requirements 7.1**

- [x] 11. Implement pagination and sort helpers
  - [x] 11.1 Create a `DatasetSortHelper` static class and `PagedResult<T>` factory method
    - Ascending/descending sort on any column key; pagination slicing at 100 rows per page
    - _Requirements: 7.3, 7.4_

  - [ ]* 11.2 Write property test: Dataset pagination invariant (Property 9)
    - **Property 9: Dataset Pagination Invariant**
    - For any N ≥ 0 rows paginated at 100 rows/page: total pages = ⌈N/100⌉; all pages except last have exactly 100 rows; no row appears on more than one page
    - **Validates: Requirements 7.4**

  - [ ]* 11.3 Write property test: Dataset sort order invariant (Property 10)
    - **Property 10: Dataset Sort Order Invariant**
    - For any collection of `DatasetRow` objects and any column key, ascending sort produces a non-decreasing sequence; descending sort produces a non-increasing sequence
    - **Validates: Requirements 7.3**

- [x] 12. Checkpoint — ensure all service-layer tests pass
  - Run `dotnet test` and confirm all unit and property-based tests pass
  - Ask the user if any behavior questions arise

- [x] 13. Wire DI and middleware in `Program.cs`
  - [x] 13.1 Register all services in `Program.cs`
    - `AddDistributedMemoryCache`, `AddSession` (8-hour idle, HttpOnly, SecurePolicy.Always), `AddHttpContextAccessor`
    - `AddHttpClient<ISlcHubClient, SlcHubClient>` with `BaseAddress` from config
    - `AddScoped<ISessionJobOrchestrator, SessionJobOrchestrator>`, `AddScoped<PreambleBuilder>`, `AddScoped<LogParserService>`
    - `AddSingleton<IMacroVarStore, MacroVarStore>`, `AddSingleton<IProgramHistoryStore, ProgramHistoryStore>`
    - `AddSignalR()`, `AddControllersWithViews` with `BearerTokenRequiredFilter`
    - Map SignalR hub: `app.MapHub<LogStreamingHub>("/hubs/log")`
    - Enable session middleware before routing
    - _Requirements: 1.1, 5.1, 12.3_

- [x] 14. Create Razor views and shared layout
  - [x] 14.1 Create `Views/Shared/_Layout.cshtml`
    - Navigation links to Editor, Dataset, History, Session; session selector showing active `SessionId`; user identity display; SignalR client script tag from CDN
    - _Requirements: 11.2_

  - [x] 14.2 Create `Views/Editor/Index.cshtml`
    - Monaco editor container div loaded from `cdn.jsdelivr.net/npm/monaco-editor`; log viewer panel with `<div id="log-container">`; "Submit" button; "Jump to Error" button
    - _Requirements: 10.1, 10.2, 10.3, 6.2, 6.3, 6.4_

  - [x] 14.3 Create `Views/Session/Index.cshtml`
    - "New Session" form (POST `/Session/New`); "Resume Session" dropdown (POST `/Session/Resume`) listing past sessions ordered by most recent first
    - _Requirements: 2.1, 2.2, 2.3_

  - [x] 14.4 Create `Views/Dataset/Index.cshtml`
    - Dataset list sidebar; sortable table with clickable column headers; prev/next pagination controls
    - _Requirements: 7.2, 7.3, 7.4_

  - [x] 14.5 Create `Views/History/Index.cshtml`
    - History list showing timestamp, 120-char code preview, log summary, datasets produced; clicking a record populates the Monaco editor
    - _Requirements: 9.2, 9.3, 9.4_

- [x] 15. Create client-side JavaScript
  - [x] 15.1 Create `wwwroot/js/log-viewer.js`
    - SignalR `HubConnectionBuilder` connecting to `/hubs/log` with `withAutomaticReconnect`; `JoinJob(jobId)` on connect; `ReceiveLog` handler → `appendLine`; `JobComplete` → `markJobDone`; SSE fallback via `EventSource` on SignalR start failure; `appendLine` assigns CSS classes `log-error`/`log-warning`/`log-note`; auto-scroll; `updateJumpToError` enables/disables the jump button
    - _Requirements: 5.2, 5.4, 6.1, 6.2, 6.3, 6.4_

  - [x] 15.2 Create `wwwroot/js/editor.js`
    - Monaco `require.config` pointing to the CDN loader; language set to `"sas"`; submit button click handler reads editor value and POSTs JSON to `/api/session-jobs`; on success stores `jobId` and calls `joinJob(jobId)` in log-viewer
    - _Requirements: 10.1, 10.2, 10.3_

  - [x] 15.3 Create `wwwroot/js/dataset.js`
    - Column-header click toggles sort direction and re-renders table rows; next/previous button handlers update `page` and fetch updated `PagedResult` from the API
    - _Requirements: 7.3, 7.4_

  - [x] 15.4 Create `wwwroot/js/macro-panel.js`
    - Renders macro vars as inline-editable name–value rows; on confirm submits `%let {name} = {newValue};` via POST to `/api/session-jobs`; on job completion re-fetches macro vars and re-renders; displays inline error message on failure
    - _Requirements: 8.2, 8.3, 8.4, 8.5_

- [x] 16. Create `wwwroot/css/site.css`
  - Plain CSS for layout, navigation, log severity colors (`.log-error`, `.log-warning`, `.log-note`), table styles, macro-panel, editor container, pagination controls — no CSS framework
  - _Requirements: 11.3_

- [x] 17. Implement `%let` assignment code generation helper
  - [x] 17.1 Add a static helper method (e.g., `MacroLetBuilder.Build(string name, string value)`) that returns `%let {name} = {value};`
    - Validates name against `[A-Z_][A-Z0-9_]*` to prevent injection; used by macro-panel JS submission and session preamble
    - _Requirements: 8.3_

  - [ ]* 17.2 Write property test: %let assignment code generation (Property 12)
    - **Property 12: %let Assignment Code Generation**
    - For any valid SAS macro variable name and any string value, `MacroLetBuilder.Build` returns exactly `%let {name} = {value};` with no extra lines
    - **Validates: Requirements 8.3**

- [ ] 18. Final checkpoint — full build and test run
  - Run `dotnet build` — zero warnings or errors
  - Run `dotnet test` — all unit and property-based tests pass
  - Verify `GET /Editor`, `GET /Session`, `GET /Dataset`, `GET /History` return HTTP 200 with a seeded session cookie (integration smoke test)
  - Ask the user if any questions arise

- [x] 19. Implement `TokenManager` service for service account authentication
  - [x] 19.1 Create `ITokenManager` interface and `TokenManager` implementation in `Services/`
    - `EnsureValidTokenAsync` checks session for user token, validates expiry, acquires new token if needed
    - `LoginAsync` calls `POST /api/v2/auth/login` with service account credentials to get impersonate token
    - `ImpersonateAsync` calls `POST /api/v2/auth/impersonate` with impersonate token and userId to get user token
    - Store `BearerToken`, `BearerTokenExpiresIn`, `BearerTokenAcquiredAt` in session
    - Throw `InvalidOperationException` on auth failures (surfaced as HTTP 503)
    - _Requirements: 1.1, 1.3, 1.4, 1.5, 1.6, 1.9, 1.10, 1.11, 1.12_

- [x] 20. Update `BearerTokenRequiredFilter` to use TokenManager
  - [x] 20.1 Change `BearerTokenRequiredFilter` to `IAsyncActionFilter` and call `TokenManager.EnsureValidTokenAsync`
    - Remove OAuth redirect logic; instead invoke TokenManager before action execution
    - Catch `InvalidOperationException` and return HTTP 503
    - _Requirements: 1.7, 1.8_
    - _Note: This replaces the stub filter from task 9.1_

- [x] 21. Update `SlcHubClient` for real Hub job API endpoints
  - [x] 21.1 Replace `PostJobAsync` and `StreamLogAsync` with new methods
    - `CreateJobAsync`: POST to `/api/v2/namespaces/{namespace}/jobs` with code and executionProfile, returns jobId
    - `CommitJobAsync`: POST to `/api/v2/namespaces/{namespace}/jobs/{jobId}/commit` to start execution
    - `GetJobStatusAsync`: GET `/api/v2/namespaces/{namespace}/jobs/{jobId}` returns status string
    - `GetJobLogAsync`: GET `/api/v2/namespaces/{namespace}/jobs/{jobId}/log` returns full log content
    - `GetJobResultsAsync`: GET `/api/v2/namespaces/{namespace}/jobs/{jobId}/results` returns list of `JobResultFile`
    - `GetResultFileContentAsync`: GET file URL, returns file content as string
    - Add `JobResultFile(string Name, string Url)` record to `Models/`
    - _Requirements: 4.3, 5.2, 5.3, 13.1, 13.2, 13.4_

- [x] 22. Update `SessionJobOrchestrator` for poll-based log flow
  - [x] 22.1 Update `SubmitAsync` to call `CreateJobAsync` then `CommitJobAsync`
    - Update `StreamAndFinalizeAsync` to poll `GetJobStatusAsync` every 5 seconds (max 10 minutes) until terminal state
    - Fetch log via `GetJobLogAsync`, split into lines, forward each to SignalR
    - Call `GetJobResultsAsync`, find stderr file if present
    - Fetch stderr content via `GetResultFileContentAsync`, parse ERROR/WARNING/NOTE lines, forward to SignalR
    - Send `JobComplete` after all lines forwarded
    - _Requirements: 4.3, 5.2, 5.3, 13.1, 13.2, 13.2b, 13.3, 13.4, 13.5, 13.6_

- [x] 23. Add startup configuration validation in Program.cs
  - [x] 23.1 Validate required appsettings keys at startup
    - Check for `SlcHub:ServiceAccount:Username`, `SlcHub:ServiceAccount:Password`, `SlcHub:UserId`, `SlcHub:BaseUrl`, `SlcHub:Namespace`, `SlcHub:ExecutionProfile`
    - Throw `InvalidOperationException` with clear message if any key is missing or empty
    - _Requirements: 1.2_

- [x] 24. Implement Working Dataset File Browser
  - [ ] 24.1 Implement `FilesApiController` in `Controllers/Api/FilesApiController.cs`
    - `GET /api/files` lists all `.sas7bdat` files in the session working directory with file size and last modified timestamp
    - `POST /api/files/{fileName}/view` submits a background job with `PROC CONTENTS SHORT` and `PROC PRINT(OBS=1000)` and returns jobId
    - Add `DatasetFileInfo(string Name, long SizeBytes, DateTime LastModified)` record to `Models/`
    - _Requirements: 8.1, 8.2, 8.4, 8.10_

  - [ ] 24.2 Create `wwwroot/js/file-browser.js`
    - `refreshFileList` fetches from `/api/files` and renders file list in Files tab
    - `viewDataset(fileName)` calls `/api/files/{fileName}/view`, subscribes to SignalR for jobId, parses PROC output, displays in modal
    - `parseAndDisplayDataset` extracts columns from PROC CONTENTS and rows from PROC PRINT
    - Implements client-side sorting and pagination (100 rows/page) for the retrieved 1,000 rows
    - Auto-refreshes file list after `JobComplete` SignalR event
    - _Requirements: 8.1, 8.2, 8.3, 8.4, 8.5, 8.6, 8.7, 8.8, 8.9_

  - [ ] 24.3 Update `Views/Editor/Index.cshtml` to add Files tab panel
    - Add a Files tab next to the Log Viewer with `<div id="files-list">` container
    - Add hidden modal `<div id="dataset-viewer-modal">` for displaying dataset contents
    - Include `<script src="~/js/file-browser.js"></script>` reference
    - _Requirements: 8.1, 8.3_

  - [ ] 24.4 Update `wwwroot/css/site.css` for file browser styles
    - Styles for `.file-item`, `.file-name`, `.file-size`, `.file-date`, `.no-files`
    - Styles for `.dataset-viewer`, `.dataset-table`, `.dataset-pagination`
    - Modal overlay and container styles for dataset viewer
    - _Requirements: 8.1, 8.3_

---

## Notes

- Tasks marked with `*` are optional and can be skipped for a faster MVP
- Property numbers match the "Correctness Properties" section of `design.md` for direct traceability
- All property-based tests use xUnit + FsCheck (or CsCheck) as specified in the design
- In-memory stores (`MacroVarStore`, `ProgramHistoryStore`) are registered as singletons — state resets on process restart, which is expected
- Monaco is loaded from CDN; no npm build pipeline is required
- The `BearerTokenRequiredFilter` is registered globally and applies to all MVC and API actions

---

## Task Dependency Graph

```json
{
  "waves": [
    { "id": 0, "tasks": ["2.1"] },
    { "id": 1, "tasks": ["3.1", "3.2", "4.1", "5.1"] },
    { "id": 2, "tasks": ["2.2", "3.3", "3.4", "3.5", "4.2", "5.2", "6.1"] },
    { "id": 3, "tasks": ["6.2", "7.1", "7.2", "11.1"] },
    { "id": 4, "tasks": ["8.1", "11.2", "11.3"] },
    { "id": 5, "tasks": ["8.2", "8.3", "9.1", "9.2", "9.3", "17.1"] },
    { "id": 6, "tasks": ["10.1", "10.2", "10.3", "10.4", "17.2"] },
    { "id": 7, "tasks": ["13.1"] },
    { "id": 8, "tasks": ["14.1", "14.2", "14.3", "14.4", "14.5"] },
    { "id": 9, "tasks": ["15.1", "15.2", "15.3", "15.4", "16"] },
    { "id": 10, "tasks": ["18"] },
    { "id": 11, "tasks": ["19.1", "21.1", "23.1"] },
    { "id": 12, "tasks": ["20.1", "22.1"] },
    { "id": 13, "tasks": ["24.1"] },
    { "id": 14, "tasks": ["24.2", "24.3", "24.4"] }
  ]
}
```
