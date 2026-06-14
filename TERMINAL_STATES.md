# Job Terminal States

## Overview

The application polls the Altair SLC Hub for job status and stops polling when a **terminal state** is reached.

## Terminal States

The application recognizes **three terminal states**:

| Status | Description | Meaning |
|--------|-------------|---------|
| `CompletedSuccess` | Job completed successfully | SAS code executed without errors |
| `CompletedError` | Job completed with errors | SAS code had runtime errors or warnings |
| `Failed` | Job failed to execute | System-level failure (e.g., timeout, resource issue) |

## Polling Logic

### Before Terminal State
```
Running → Running → Running → ...
```
- Polls every 5 seconds
- Maximum timeout: 10 minutes

### After Terminal State
```
Running → CompletedSuccess ✓ (Stop polling)
Running → CompletedError   ✓ (Stop polling)  
Running → Failed           ✓ (Stop polling)
```

## Implementation

### SessionJobOrchestrator.cs

```csharp
while (status != "CompletedSuccess" && 
       status != "CompletedError" && 
       status != "Failed")
{
    if (DateTime.UtcNow - startTime > maxWaitTime)
    {
        logger.LogWarning("Job {JobId} timed out after {MaxWait}", jobId, maxWaitTime);
        break;
    }
    await Task.Delay(pollInterval, ct);
    status = await hubClient.GetJobStatusAsync(jobId, ct);
}
```

### SseLogController.cs

```csharp
while (status != "CompletedSuccess" && 
       status != "CompletedError" && 
       status != "Failed")
{
    if (DateTime.UtcNow - startTime > maxWaitTime)
    {
        logger.LogWarning("SSE stream for job {JobId} timed out...", jobId, maxWaitTime);
        await Response.WriteAsync($"data: {{\"error\": \"Job polling timeout\"}}\n\n", ct);
        return;
    }
    await Task.Delay(pollInterval, ct);
    status = await hubClient.GetJobStatusAsync(jobId, ct);
    
    // Send status updates
    await Response.WriteAsync($"data: {{\"status\": \"{status}\"}}\n\n", ct);
    await Response.Body.FlushAsync(ct);
}
```

## Status Flow Examples

### Success Scenario
```
1. Submit job
2. Status: "Running"
3. Status: "Running" (after 5s)
4. Status: "Running" (after 10s)
5. Status: "CompletedSuccess" ✓
6. Stop polling
7. Fetch logs
8. Process results
```

### Error Scenario
```
1. Submit job
2. Status: "Running"
3. Status: "Running" (after 5s)
4. Status: "CompletedError" ✓
5. Stop polling
6. Fetch logs (will contain ERROR lines)
7. Process results
```

### Failure Scenario
```
1. Submit job
2. Status: "Running"
3. Status: "Failed" ✓
4. Stop polling
5. Fetch logs (may contain system error)
6. Handle failure
```

## Post-Polling Actions

After a terminal state is reached, the application:

1. **Fetches Complete Log**
   ```csharp
   var logContent = await hubClient.GetJobLogAsync(jobId, ct);
   ```

2. **Streams Log Lines to SignalR**
   ```csharp
   foreach (var line in logContent.Split('\n'))
   {
       await signalrContext.Clients.Group(jobId)
           .SendAsync("ReceiveLog", line, ct);
   }
   ```

3. **Fetches Result Files** (if available)
   ```csharp
   var results = await hubClient.GetJobResultsAsync(jobId, ct);
   ```

4. **Processes stderr** (errors/warnings)
   ```csharp
   var stderrFile = results.FirstOrDefault(f => 
       f.Name.Equals("stderr", StringComparison.OrdinalIgnoreCase));
   ```

5. **Sends Completion Signal**
   ```csharp
   await signalrContext.Clients.Group(jobId)
       .SendAsync("JobComplete", ct);
   ```

6. **Parses and Stores Macro Variables**
   ```csharp
   var newVars = logParser.ParseUserMacroVars(logLines);
   if (newVars.Count > 0)
       await macroVarStore.SetAsync(sessionId, newVars);
   ```

7. **Persists History Record**
   ```csharp
   await historyStore.AddAsync(new ProgramHistoryRecord(...));
   ```

## Timeout Handling

If no terminal state is reached within 10 minutes:

```csharp
if (DateTime.UtcNow - startTime > maxWaitTime)
{
    logger.LogWarning("Job {JobId} timed out after {MaxWait}", jobId, maxWaitTime);
    break; // Exit polling loop
}
```

The application will:
- Stop polling
- Log a warning
- Attempt to fetch whatever logs are available
- Send error notification to client

## Client-Side Handling

### SignalR Messages

Clients receive these messages:

| Message | When | Data |
|---------|------|------|
| `ReceiveLog` | For each log line | `string line` |
| `JobComplete` | On success | None |
| `JobError` | On error/failure | `string errorMessage` |

### SSE Messages

SSE clients receive:

| Message | When | Format |
|---------|------|--------|
| Status update | Each poll | `{"status": "Running"}` |
| Log line | After terminal state | Raw log line |
| Completion | After logs | `{"complete": true}` |
| Error | On timeout/error | `{"error": "message"}` |

## Status Transitions (SLC Hub)

The SLC Hub may report these status transitions:

```
Created → Running → CompletedSuccess
Created → Running → CompletedError
Created → Running → Failed
Created → Failed (immediate failure)
```

## Configuration

### Polling Interval
```csharp
var pollInterval = TimeSpan.FromSeconds(5);
```

### Maximum Wait Time
```csharp
var maxWaitTime = TimeSpan.FromMinutes(10);
```

These are currently hardcoded but can be made configurable if needed.

## Testing Terminal States

### Test CompletedSuccess
```sas
/* Simple successful code */
data test;
    x = 1;
run;
```
Expected: Status transitions to `CompletedSuccess`

### Test CompletedError
```sas
/* Code with intentional error */
data test;
    x = 1 / 0;  /* Division by zero */
run;
```
Expected: Status transitions to `CompletedError`

### Test Failed
- Submit job with invalid code
- Or trigger system-level failure
Expected: Status transitions to `Failed`

## Debugging

### Check Logs

Look for these log entries:

**Normal completion:**
```
(No special log - polling just stops)
```

**Timeout:**
```
warn: SessionJobOrchestrator[0]
      Job abc123 timed out after 00:10:00
```

**Error:**
```
fail: SessionJobOrchestrator[0]
      Error streaming job abc123
      System.Exception: ...
```

### Status History

The application doesn't currently log status changes, but you can add logging:

```csharp
status = await hubClient.GetJobStatusAsync(jobId, ct);
logger.LogDebug("Job {JobId} status: {Status}", jobId, status);
```

## Summary

✅ **Three terminal states**: `CompletedSuccess`, `CompletedError`, `Failed`
✅ **Polling stops** when any terminal state is reached
✅ **5-second intervals** with 10-minute timeout
✅ **Logs are fetched** after terminal state
✅ **Results processed** regardless of success/error/failure

The application handles all three terminal states gracefully and continues processing (log fetching, macro variable parsing, history storage) even when errors occur.
