# Requirements Document

## Introduction

SAS_Job_Runner is an ASP.NET Core MVC application targeting .NET 10 that wraps the Altair SLC Hub REST API. It provides a multi-page web interface for authoring SAS programs in a Monaco editor, submitting jobs, streaming real-time logs, exploring output datasets, managing macro variables, and reviewing program history. Authentication is handled via a service account stored in appsettings: the application logs in automatically, impersonates the configured user, and stores the resulting Bearer token in ASP.NET Session for all subsequent Hub requests. Persistence (Redis for macro variables, PostgreSQL for program history) is mocked in-memory for this phase.

---

## Glossary

- **SAS_Job_Runner**: The ASP.NET Core MVC application being built.
- **Altair SLC Hub**: The external REST API that executes SAS programs.
- **SlcHubClient**: The typed `HttpClient` wrapper that communicates with the Altair SLC Hub.
- **TokenManager**: The singleton service responsible for obtaining and refreshing the service-account-based Bearer token.
- **SessionJobOrchestrator**: The service responsible for assembling and submitting full SAS programs to the Altair SLC Hub.
- **SessionJobApiController**: The ASP.NET Core API controller exposing job-submission endpoints under `/api/session-jobs`.
- **LogStreamingHub**: The SignalR hub hosted at `/hubs/log` that pushes real-time log lines to connected clients.
- **SseLogController**: The ASP.NET Core controller providing the SSE fallback endpoint for log streaming.
- **MacroVarStore**: The mocked Redis service that persists and retrieves SAS macro variable state per session.
- **ProgramHistoryStore**: The mocked PostgreSQL service that persists and retrieves program execution history per user.
- **MonacoEditor**: The browser-based code editor component used for SAS source input.
- **SessionId**: A UUID that uniquely identifies a SAS session for a given user.
- **UserId**: The identifier of the user to impersonate, read from `SlcHub:UserId` in appsettings.
- **Session_Preamble**: The auto-generated SAS code block injected before user code, containing `LIBNAME SESSLIB`, `%let SESSIONID`, and restored macro variable assignments.
- **Bearer Token**: The user token (impersonation token) stored in ASP.NET Session under the key `"BearerToken"` and forwarded as the `Authorization: Bearer` header on every Hub request.
- **Impersonate Token**: The short-lived token returned by `POST /api/v2/auth/login` used exclusively to call `POST /api/v2/auth/impersonate`.
- **User Token**: The token returned by `POST /api/v2/auth/impersonate`; this is the Bearer Token stored in session and used on all Hub API calls.
- **DatasetExplorer**: The UI component that displays SAS output dataset contents with sorting and pagination.
- **FileBrowser**: The UI component in the Editor view that lists `.sas7bdat` dataset files in the session working directory.
- **DatasetViewer**: The modal or panel that displays the contents of a selected dataset file from the FileBrowser.
- **MacroVarPanel**: The UI component that lists current macro variables and allows inline editing.
- **ProgramHistory**: The record of a past program submission including timestamp, source code, log summary, and datasets produced.
- **Namespace**: The Altair SLC Hub namespace under which jobs are created, read from `SlcHub:Namespace` in appsettings.
- **ExecutionProfile**: The execution profile name passed when creating a job, read from `SlcHub:ExecutionProfile` in appsettings.
- **JobStatus**: The state of a submitted job as reported by the Hub; terminal states are `Completed` and `Failed`.

---

## Requirements

### Requirement 1: Service Account Authentication and User Impersonation

**User Story:** As a system administrator deploying SAS_Job_Runner, I want the application to authenticate automatically using a configured service account and impersonate the target user, so that no manual login step is required and all Hub requests are made on behalf of the correct user.

#### Acceptance Criteria

1. WHEN the application starts, THE SAS_Job_Runner SHALL read service account credentials (`SlcHub:ServiceAccount:Username`, `SlcHub:ServiceAccount:Password`), the target UserId (`SlcHub:UserId`), and the Hub base URL (`SlcHub:BaseUrl`) from appsettings.
2. IF any of the required appsettings keys (`SlcHub:ServiceAccount:Username`, `SlcHub:ServiceAccount:Password`, `SlcHub:UserId`, `SlcHub:BaseUrl`) are missing or empty at startup, THEN THE SAS_Job_Runner SHALL abort startup, log which key is missing, and throw a configuration exception.
3. WHEN the application requires a valid Bearer Token and none is present in session, THE TokenManager SHALL call `POST /api/v2/auth/login` with body `{ "username": "...", "password": "..." }` using the configured service account credentials to obtain the Impersonate Token.
4. WHEN the TokenManager has obtained a valid Impersonate Token, THE TokenManager SHALL call `POST /api/v2/auth/impersonate` with header `Authorization: Bearer {impersonateToken}` and body `{ "userId": "..." }` using the configured UserId to obtain the User Token.
5. WHEN the TokenManager obtains a User Token, THE TokenManager SHALL store the User Token, its `expiresIn` value (in seconds), and the UTC timestamp of acquisition in ASP.NET Session under the keys `"BearerToken"`, `"BearerTokenExpiresIn"`, and `"BearerTokenAcquiredAt"` respectively.
6. WHEN SlcHubClient sends any HTTP request to the Altair SLC Hub, THE SlcHubClient SHALL attach the `Authorization: Bearer {token}` header using the User Token retrieved from ASP.NET Session.
7. IF the Bearer Token is absent from ASP.NET Session, THEN THE BearerTokenRequiredFilter SHALL invoke the TokenManager to acquire a new User Token before the request proceeds, instead of redirecting to a login page.
8. WHEN the TokenManager has acquired a new User Token, THE BearerTokenRequiredFilter SHALL allow the original request to proceed without interruption.
9. IF the elapsed time since `"BearerTokenAcquiredAt"` equals or exceeds the stored `"BearerTokenExpiresIn"` duration, THEN THE TokenManager SHALL refresh the User Token by repeating the impersonate call (`POST /api/v2/auth/impersonate`) using the current Impersonate Token, without requiring a new service account login.
10. IF the elapsed time since the Impersonate Token was acquired equals or exceeds its `expiresIn` duration, THEN THE TokenManager SHALL re-authenticate the service account by calling `POST /api/v2/auth/login` again to obtain a new Impersonate Token, then obtain a new User Token via `POST /api/v2/auth/impersonate`.
11. IF `POST /api/v2/auth/login` returns a non-success HTTP status, THEN THE TokenManager SHALL log the error and throw an authentication exception that surfaces as an HTTP 503 response to the caller.
12. IF `POST /api/v2/auth/impersonate` returns a non-success HTTP status, THEN THE TokenManager SHALL log the error and throw an authentication exception that surfaces as an HTTP 503 response to the caller.

---

### Requirement 2: Session Lifecycle Management

**User Story:** As a SAS author, I want to create a new SAS session or resume a past one, so that my macro variable state and session library are preserved across job submissions.

#### Acceptance Criteria

1. WHEN a user clicks "New Session", THE SAS_Job_Runner SHALL generate a new UUID SessionId and create a server-side folder at `/sas/sessions/{UserId}/{SessionId}/`.
2. WHEN a user selects a past session from the "Resume Session" dropdown, THE SAS_Job_Runner SHALL load the most recently persisted macro variable state for that SessionId from MacroVarStore.
3. THE SAS_Job_Runner SHALL list all past sessions for the current user in the "Resume Session" dropdown, ordered by most recent timestamp first.
4. THE SAS_Job_Runner SHALL associate every job submission with the active SessionId.

---

### Requirement 3: Session Preamble Injection

**User Story:** As a SAS author, I want the session context (library name, session ID, macro variables) automatically prepended to my code, so that every job runs within the correct session environment without manual setup.

#### Acceptance Criteria

1. WHEN SessionJobOrchestrator assembles a full program, THE SessionJobOrchestrator SHALL prepend the Session_Preamble before the user-supplied code.
2. THE SessionJobOrchestrator SHALL include a `LIBNAME SESSLIB` statement pointing to the session folder in the Session_Preamble.
3. THE SessionJobOrchestrator SHALL include a `%let SESSIONID = {SessionId};` statement in the Session_Preamble.
4. WHEN MacroVarStore contains persisted macro variables for the active SessionId, THE SessionJobOrchestrator SHALL emit one `%let {name} = {value};` statement per variable in the Session_Preamble.
5. THE SessionJobOrchestrator SHALL append a `%put _user_;` trailer after the user-supplied code to capture all user macro variables in the job log.

---

### Requirement 4: Job Submission via SessionJobApiController

**User Story:** As a SAS author, I want to submit SAS code through a REST endpoint, so that the application can orchestrate the two-step job creation and commit flow and return a job handle for log tracking.

#### Acceptance Criteria

1. THE SessionJobApiController SHALL expose a `POST /api/session-jobs` endpoint that accepts a request body containing a `SourceCode` string (1–1,000,000 characters) and a `SessionId` string (1–256 characters), with `[Required]` validation attributes on both fields; the UserId shall be read from server-side ASP.NET Session, not from the request body.
2. WHEN `POST /api/session-jobs` is called, THE SessionJobApiController SHALL delegate assembly and submission to SessionJobOrchestrator.
3. WHEN SessionJobOrchestrator submits the assembled program, THE SessionJobOrchestrator SHALL call `SlcHubClient.PostJobAsync` which encapsulates creating a job draft (`POST /api/v2/namespaces/{namespace}/jobs` with SAS program source and configured ExecutionProfile) and committing it (`POST /api/v2/namespaces/{namespace}/jobs/{jobId}/commit`), returning the jobId on success.
4. WHEN the commit call succeeds, THE SessionJobApiController SHALL return HTTP 200 with body `{ "jobId": "..." }` to the caller.
5. IF the `JobSubmitRequest` fails model validation (missing or empty `SourceCode` or `SessionId`), THEN THE SessionJobApiController SHALL return HTTP 400 with a validation error body before delegating to the orchestrator.
6. IF the UserId is absent from ASP.NET Session, THEN THE SessionJobApiController SHALL return HTTP 400 with an error message indicating the UserId is missing.
7. IF `SlcHubClient.PostJobAsync` throws a `SlcHubException`, THEN THE SessionJobApiController SHALL return HTTP 502 with the Hub error message from the exception.

---

### Requirement 5: Job Status Polling and Real-Time Log Streaming

**User Story:** As a SAS author, I want to see log lines appear in real time as my job runs, so that I can monitor progress and detect errors without waiting for job completion.

#### Acceptance Criteria

1. THE SAS_Job_Runner SHALL host LogStreamingHub as a SignalR hub at `/hubs/log`.
2. WHEN a job has been committed, THE SessionJobOrchestrator SHALL start a background task that polls `GET /api/v2/namespaces/{namespace}/jobs/{jobId}` via SlcHubClient at 5-second intervals until the JobStatus reaches a terminal state (`Completed` or `Failed`), with a maximum polling timeout of 10 minutes.
3. WHEN the JobStatus reaches a terminal state, THE SessionJobOrchestrator SHALL fetch the job log by calling `GET /api/v2/namespaces/{namespace}/jobs/{jobId}/log` via SlcHubClient.
4. WHEN the log content is retrieved, THE SessionJobOrchestrator SHALL split the content into individual lines, classify each line using `LogLine.Parse`, and forward each line to LogStreamingHub by calling `SendAsync("ReceiveLog", line, ct)` for broadcast to subscribed SignalR clients.
5. WHEN SessionJobOrchestrator forwards a log line to LogStreamingHub, THE LogStreamingHub SHALL push the log line via the `"ReceiveLog"` method to all SignalR clients subscribed to that job's group.
6. WHEN a SignalR connection cannot be established by the client-side JavaScript, THE JavaScript client SHALL fall back to the SSE endpoint provided by SseLogController automatically.
7. THE SseLogController SHALL expose a `GET /api/session-jobs/{jobId}/log-stream` endpoint that streams log lines as Server-Sent Events.
8. WHEN the SessionJobOrchestrator has forwarded all log lines and result diagnostics (if any), THE SessionJobOrchestrator SHALL send a `"JobComplete"` SignalR message to all clients subscribed to that job's group; IF an error occurs during polling or log retrieval, THE SessionJobOrchestrator SHALL send a `"JobError"` SignalR message with the error details.

---

### Requirement 6: Log Viewer UI

**User Story:** As a SAS author, I want log lines color-coded by severity and to be able to jump directly to the first error, so that I can quickly diagnose job failures.

#### Acceptance Criteria

1. THE SAS_Job_Runner SHALL render log lines in the Log Viewer with distinct colors: red for lines prefixed `ERROR`, yellow for `WARNING`, blue for `NOTE`, and the default text color for all other lines.
2. WHEN new log lines arrive via SignalR or SSE, THE SAS_Job_Runner SHALL automatically scroll the Log Viewer to the most recently received line.
3. THE SAS_Job_Runner SHALL display a "Jump to Error" button in the Log Viewer that, when clicked, scrolls the view to the first `ERROR` line in the current log.
4. IF no `ERROR` lines are present in the current log, THEN THE SAS_Job_Runner SHALL disable the "Jump to Error" button.

---

### Requirement 7: Dataset Explorer

**User Story:** As a SAS author, I want to click on an output dataset and see its columns and first 100 rows, so that I can quickly inspect job results.

#### Acceptance Criteria

1. WHEN a user clicks on a dataset name in the Dataset Explorer, THE SAS_Job_Runner SHALL submit a job containing `PROC CONTENTS` followed by `PROC PRINT DATA={dataset}(OBS=100)` via SessionJobOrchestrator.
2. WHEN the dataset introspection job completes, THE SAS_Job_Runner SHALL render the result in a tabular view within the Dataset Explorer.
3. THE SAS_Job_Runner SHALL support column-header click to sort the displayed dataset rows in ascending order; a second click on the same header SHALL sort in descending order.
4. THE SAS_Job_Runner SHALL paginate the dataset display at 100 rows per page and provide next/previous page controls.

---

### Requirement 8: Working Dataset File Browser

**User Story:** As a SAS author, I want to browse the working dataset files produced by my program and view their contents in a tabular viewer, so that I can inspect output datasets without writing additional `PROC PRINT` code.

#### Acceptance Criteria

1. THE SAS_Job_Runner SHALL display a "Files" tab or panel in the Editor view that lists all `.sas7bdat` dataset files in the session working directory (`/sas/sessions/{UserId}/{SessionId}/`).
2. WHEN a job completes successfully, THE SAS_Job_Runner SHALL automatically refresh the Files tab to show any newly created dataset files.
3. WHEN a user clicks on a dataset file name in the Files tab, THE SAS_Job_Runner SHALL open a Dataset Viewer modal or panel that displays the dataset contents.
4. WHEN the Dataset Viewer opens, THE SAS_Job_Runner SHALL submit a background job containing `PROC CONTENTS DATA=SESSLIB.{datasetName} SHORT;` followed by `PROC PRINT DATA=SESSLIB.{datasetName}(OBS=1000);` via SessionJobOrchestrator to retrieve the dataset structure and first 1,000 rows.
5. WHEN the dataset introspection job completes, THE SAS_Job_Runner SHALL parse the `PROC CONTENTS` output to extract column names and types, then parse the `PROC PRINT` output to extract row data, and render them in a tabular view with column headers.
6. THE Dataset Viewer SHALL support column-header click to sort the displayed rows in ascending order; a second click on the same header SHALL sort in descending order (client-side sorting on the retrieved 1,000 rows).
7. THE Dataset Viewer SHALL paginate the display at 100 rows per page and provide next/previous page controls.
8. THE Dataset Viewer SHALL display the total row count retrieved (up to 1,000) and indicate if more rows exist in the dataset.
9. IF the dataset file does not exist or the introspection job fails, THEN THE SAS_Job_Runner SHALL display an error message in the Dataset Viewer indicating the dataset could not be loaded.
10. THE Files tab SHALL display the file size (in KB/MB) and last modified timestamp for each dataset file.

---

### Requirement 9: Macro Variable Panel

**User Story:** As a SAS author, I want to see my current macro variables and edit them inline, so that I can adjust session state without rewriting and resubmitting entire programs.

#### Acceptance Criteria

1. WHEN a job completes, THE SAS_Job_Runner SHALL parse the `%put _user_;` output from the job log and update the MacroVarPanel with the current macro variable names and values.
2. THE MacroVarPanel SHALL display all current macro variables as a list of name–value pairs.
3. WHEN a user edits a macro variable value inline in MacroVarPanel and confirms, THE SAS_Job_Runner SHALL submit a job containing `%let {name} = {newValue};` via SessionJobOrchestrator to apply the change.
4. WHEN a macro variable assignment job completes successfully, THE SAS_Job_Runner SHALL update MacroVarStore with the new value for that variable under the active SessionId.
5. IF the macro variable assignment job fails, THEN THE SAS_Job_Runner SHALL display an inline error message next to the edited variable in MacroVarPanel.

---

### Requirement 10: Program History

**User Story:** As a SAS author, I want to review past program submissions and reload earlier code into the editor, so that I can rerun or iterate on previous work.

#### Acceptance Criteria

1. WHEN a job completes, THE SAS_Job_Runner SHALL persist a ProgramHistory record to ProgramHistoryStore containing: the UserId, SessionId, submission timestamp, SAS source code (user portion only), a log summary (first ERROR or WARNING line, or "Completed" if none), and the list of datasets produced.
2. THE SAS_Job_Runner SHALL display ProgramHistory records in the History view ordered by most recent submission timestamp first.
3. THE SAS_Job_Runner SHALL display the submission timestamp, a truncated code preview (first 120 characters), log summary, and datasets produced for each ProgramHistory record.
4. WHEN a user clicks a ProgramHistory record, THE SAS_Job_Runner SHALL load the stored SAS source code into the MonacoEditor.

---

### Requirement 11: Monaco Editor Integration

**User Story:** As a SAS author, I want to write SAS code in a feature-rich browser editor, so that I benefit from syntax highlighting and a comfortable editing experience.

#### Acceptance Criteria

1. THE SAS_Job_Runner SHALL embed the MonacoEditor on the code-authoring view.
2. THE SAS_Job_Runner SHALL configure the MonacoEditor with SAS language syntax highlighting.
3. WHEN the user clicks the submit button on the editor view, THE SAS_Job_Runner SHALL read the current MonacoEditor content and POST it to `POST /api/session-jobs`.
4. WHEN a ProgramHistory record is loaded, THE SAS_Job_Runner SHALL set the MonacoEditor content to the stored SAS source code.

---

### Requirement 12: Multi-Page MVC Layout

**User Story:** As a user of SAS_Job_Runner, I want distinct pages for each major function, so that each feature has a dedicated, uncluttered view.

#### Acceptance Criteria

1. THE SAS_Job_Runner SHALL provide separate MVC views and routes for: the SAS code editor, the Dataset Explorer, the Program History list, and the Session management UI.
2. THE SAS_Job_Runner SHALL render all views using a shared layout template that includes the navigation menu, session selector, and user identity display.
3. THE SAS_Job_Runner SHALL style all views using plain CSS with no CSS framework dependency.

---

### Requirement 13: Mocked Persistence Layer

**User Story:** As a developer building SAS_Job_Runner, I want Redis and PostgreSQL replaced by in-memory mocks, so that the application runs without external infrastructure during this phase.

#### Acceptance Criteria

1. THE MacroVarStore SHALL store and retrieve macro variable maps in process memory, keyed by SessionId, without connecting to an external Redis instance.
2. THE ProgramHistoryStore SHALL store and retrieve ProgramHistory records in process memory, keyed by UserId, without connecting to an external PostgreSQL instance.
3. THE SAS_Job_Runner SHALL register MacroVarStore and ProgramHistoryStore as singleton services via ASP.NET Core dependency injection so that state persists for the lifetime of the application process.
4. IF the application process restarts, THEN THE SAS_Job_Runner SHALL start with empty MacroVarStore and ProgramHistoryStore state, which is the expected behavior for in-memory mocks.

---

### Requirement 14: Job Result File Retrieval and Diagnostic Surfacing

**User Story:** As a SAS author, I want the application to retrieve stdout, stderr, and result files after a job completes, so that ERROR, WARNING, and NOTE lines from stderr supplement the log output and are surfaced correctly in the UI.

#### Acceptance Criteria

1. WHEN a job reaches a terminal JobStatus, THE SessionJobOrchestrator SHALL call `GET /api/v2/namespaces/{namespace}/jobs/{jobId}/results` via SlcHubClient to retrieve the result file listing.
2. WHEN the results endpoint returns a result file listing containing a `stderr` entry, THE SessionJobOrchestrator SHALL call `SlcHubClient.GetResultFileContentAsync` with the file URL to retrieve the stderr content, then scan it line-by-line using `LogLine.Parse` to identify lines with `Error`, `Warning`, or `Note` severity (case-insensitive prefix matching as per `LogLine.Parse` logic).
2b. WHEN the results endpoint returns a result file listing that does NOT contain a `stderr` entry, THE SessionJobOrchestrator SHALL skip stderr processing and proceed to the next step.
3. WHEN diagnostic lines are found in stderr, THE SessionJobOrchestrator SHALL append those lines to the log line collection after the main log content and before sending `"JobComplete"`, then forward each diagnostic line to LogStreamingHub via `SendAsync("ReceiveLog", line, ct)` so they appear in the Log Viewer alongside regular log output.
4. WHEN the results endpoint returns a result file listing containing a `stdout` entry, THE SessionJobOrchestrator SHALL call `SlcHubClient.GetResultFileContentAsync` with the file URL to retrieve the stdout content, then forward the first 10,000 lines as individual `"ReceiveLog"` SignalR messages to make the stdout content observable in the Log Viewer.
5. IF the results endpoint returns a non-success HTTP status, THEN THE SessionJobOrchestrator SHALL log the error and continue finalizing the job without treating the missing results as a fatal failure.
6. WHEN the log content and stderr diagnostics have been fully forwarded to LogStreamingHub, THE SessionJobOrchestrator SHALL send a `"JobComplete"` SignalR message to all clients subscribed to that job's group.

**User Story:** As a SAS author, I want to see my current macro variables and edit them inline, so that I can adjust session state without rewriting and resubmitting entire programs.

#### Acceptance Criteria

1. WHEN a job completes, THE SAS_Job_Runner SHALL parse the `%put _user_;` output from the job log and update the MacroVarPanel with the current macro variable names and values.
2. THE MacroVarPanel SHALL display all current macro variables as a list of name–value pairs.
3. WHEN a user edits a macro variable value inline in MacroVarPanel and confirms, THE SAS_Job_Runner SHALL submit a job containing `%let {name} = {newValue};` via SessionJobOrchestrator to apply the change.
4. WHEN a macro variable assignment job completes successfully, THE SAS_Job_Runner SHALL update MacroVarStore with the new value for that variable under the active SessionId.
5. IF the macro variable assignment job fails, THEN THE SAS_Job_Runner SHALL display an inline error message next to the edited variable in MacroVarPanel.

---

### Requirement 9: Program History

**User Story:** As a SAS author, I want to review past program submissions and reload earlier code into the editor, so that I can rerun or iterate on previous work.

#### Acceptance Criteria

1. WHEN a job completes, THE SAS_Job_Runner SHALL persist a ProgramHistory record to ProgramHistoryStore containing: the UserId, SessionId, submission timestamp, SAS source code (user portion only), a log summary (first ERROR or WARNING line, or "Completed" if none), and the list of datasets produced.
2. THE SAS_Job_Runner SHALL display ProgramHistory records in the History view ordered by most recent submission timestamp first.
3. THE SAS_Job_Runner SHALL display the submission timestamp, a truncated code preview (first 120 characters), log summary, and datasets produced for each ProgramHistory record.
4. WHEN a user clicks a ProgramHistory record, THE SAS_Job_Runner SHALL load the stored SAS source code into the MonacoEditor.

---

### Requirement 10: Monaco Editor Integration

**User Story:** As a SAS author, I want to write SAS code in a feature-rich browser editor, so that I benefit from syntax highlighting and a comfortable editing experience.

#### Acceptance Criteria

1. THE SAS_Job_Runner SHALL embed the MonacoEditor on the code-authoring view.
2. THE SAS_Job_Runner SHALL configure the MonacoEditor with SAS language syntax highlighting.
3. WHEN the user clicks the submit button on the editor view, THE SAS_Job_Runner SHALL read the current MonacoEditor content and POST it to `POST /api/session-jobs`.
4. WHEN a ProgramHistory record is loaded, THE SAS_Job_Runner SHALL set the MonacoEditor content to the stored SAS source code.

---

### Requirement 11: Multi-Page MVC Layout

**User Story:** As a user of SAS_Job_Runner, I want distinct pages for each major function, so that each feature has a dedicated, uncluttered view.

#### Acceptance Criteria

1. THE SAS_Job_Runner SHALL provide separate MVC views and routes for: the SAS code editor, the Dataset Explorer, the Program History list, and the Session management UI.
2. THE SAS_Job_Runner SHALL render all views using a shared layout template that includes the navigation menu, session selector, and user identity display.
3. THE SAS_Job_Runner SHALL style all views using plain CSS with no CSS framework dependency.

---

### Requirement 12: Mocked Persistence Layer

**User Story:** As a developer building SAS_Job_Runner, I want Redis and PostgreSQL replaced by in-memory mocks, so that the application runs without external infrastructure during this phase.

#### Acceptance Criteria

1. THE MacroVarStore SHALL store and retrieve macro variable maps in process memory, keyed by SessionId, without connecting to an external Redis instance.
2. THE ProgramHistoryStore SHALL store and retrieve ProgramHistory records in process memory, keyed by UserId, without connecting to an external PostgreSQL instance.
3. THE SAS_Job_Runner SHALL register MacroVarStore and ProgramHistoryStore as singleton services via ASP.NET Core dependency injection so that state persists for the lifetime of the application process.
4. IF the application process restarts, THEN THE SAS_Job_Runner SHALL start with empty MacroVarStore and ProgramHistoryStore state, which is the expected behavior for in-memory mocks.

---

### Requirement 13: Job Result File Retrieval and Diagnostic Surfacing

**User Story:** As a SAS author, I want the application to retrieve stdout, stderr, and result files after a job completes, so that ERROR, WARNING, and NOTE lines from stderr supplement the log output and are surfaced correctly in the UI.

#### Acceptance Criteria

1. WHEN a job reaches a terminal JobStatus, THE SessionJobOrchestrator SHALL call `GET /api/v2/namespaces/{namespace}/jobs/{jobId}/results` via SlcHubClient to retrieve the result file listing.
2. WHEN the results endpoint returns a result file listing containing a `stderr` entry, THE SessionJobOrchestrator SHALL call `SlcHubClient.GetResultFileContentAsync` with the file URL to retrieve the stderr content, then scan it line-by-line using `LogLine.Parse` to identify lines with `Error`, `Warning`, or `Note` severity (case-insensitive prefix matching as per `LogLine.Parse` logic).
2b. WHEN the results endpoint returns a result file listing that does NOT contain a `stderr` entry, THE SessionJobOrchestrator SHALL skip stderr processing and proceed to the next step.
3. WHEN diagnostic lines are found in stderr, THE SessionJobOrchestrator SHALL append those lines to the log line collection after the main log content and before sending `"JobComplete"`, then forward each diagnostic line to LogStreamingHub via `SendAsync("ReceiveLog", line, ct)` so they appear in the Log Viewer alongside regular log output.
4. WHEN the results endpoint returns a result file listing containing a `stdout` entry, THE SessionJobOrchestrator SHALL call `SlcHubClient.GetResultFileContentAsync` with the file URL to retrieve the stdout content, then forward the first 10,000 lines as individual `"ReceiveLog"` SignalR messages to make the stdout content observable in the Log Viewer.
5. IF the results endpoint returns a non-success HTTP status, THEN THE SessionJobOrchestrator SHALL log the error and continue finalizing the job without treating the missing results as a fatal failure.
6. WHEN the log content and stderr diagnostics have been fully forwarded to LogStreamingHub, THE SessionJobOrchestrator SHALL send a `"JobComplete"` SignalR message to all clients subscribed to that job's group.
