# Troubleshooting: Missing Logs for CompletedError

## Issue

Logs appear for jobs with `CompletedSuccess` status, but not for jobs with `CompletedError` status.

## Root Causes

### 1. Main Log API Returns Error for CompletedError

The `/api/v2/namespaces/{ns}/jobs/{jobId}/log` endpoint may:
- Return HTTP 404 (Not Found) for error jobs
- Return HTTP 500 (Internal Server Error) 
- Return empty content
- Return an error message instead of the actual log

### 2. Logs Are in Result Files (stdout/stderr)

For `CompletedError` jobs, the SLC Hub may store logs in **result files** instead of the main log endpoint:
- **stdout** - Contains the actual SAS log output
- **stderr** - Contains errors and warnings

## Solution Implemented

The application now has **fallback log retrieval**:

### Step 1: Try Main Log Endpoint
```csharp
try {
    var logContent = await hubClient.GetJobLogAsync(jobId, ct);
    // Stream log lines to client
}
catch (SlcHubException ex) {
    // Log the error but continue
    // Try result files next
}
```

### Step 2: Try Result Files (stdout)
```csharp
var stdoutFile = results.FirstOrDefault(f => 
    f.Name.Equals("stdout", StringComparison.OrdinalIgnoreCase));
if (stdoutFile is not null) {
    var stdoutContent = await hubClient.GetResultFileContentAsync(stdoutFile.Url, ct);
    // Stream all stdout lines to client
}
```

### Step 3: Try Result Files (stderr)
```csharp
var stderrFile = results.FirstOrDefault(f => 
    f.Name.Equals("stderr", StringComparison.OrdinalIgnoreCase));
if (stderrFile is not null) {
    var stderrContent = await hubClient.GetResultFileContentAsync(stderrFile.Url, ct);
    // Stream ERROR/WARNING lines to client
}
```

### Step 4: Send Warning if No Logs Found
```csharp
if (no logs from any source) {
    await signalrContext.Clients.Group(jobId)
        .SendAsync("ReceiveLog", "WARNING: No log content available for this job", ct);
}
```

## How to Verify the Fix

### Test CompletedError Scenario

1. **Submit error-prone code:**
   ```sas
   /* This will cause an error */
   data test;
       x = 1 / 0;  /* Division by zero */
   run;
   ```

2. **Check application logs** for these entries:
   ```
   info: SessionJobOrchestrator[0]
         Job abc123 reached terminal state: CompletedError
   
   (If main log fails:)
   fail: SessionJobOrchestrator[0]
         Job abc123: Failed to fetch job log. Status code: 404. Message: ...
   
   info: SessionJobOrchestrator[0]
         Job abc123: Found 2 result files
   
   info: SessionJobOrchestrator[0]
         Job abc123: Processing stdout file from http://...
   
   info: SessionJobOrchestrator[0]
         Job abc123: Processed 150 lines from stdout
   ```

3. **Check browser console** - Should see log lines received via SignalR

4. **Check UI** - Should display the log content

## Diagnostic Logging

The application now logs detailed information about log retrieval:

### Successful Main Log Fetch
```
info: Job abc123: Streamed 200 log lines from main log
```

### Failed Main Log Fetch
```
fail: Job abc123: Failed to fetch job log. Status code: 404
warn: Job abc123: Unable to fetch main job log (HTTP 404)
```

### Result Files Processing
```
info: Job abc123: Found 2 result files
info: Job abc123: Processing stdout file from http://hub.example.com/results/stdout
info: Job abc123: Processed 150 lines from stdout
info: Job abc123: Processing stderr file from http://hub.example.com/results/stderr
info: Job abc123: Processed stderr content, 25 lines total
```

### No Logs Available
```
warn: Job abc123: No log content found in main log, stdout, or stderr
```

## Checking Application Logs

Run the application and check the console output:

```powershell
cd SasJobRunner
dotnet run
```

When a job completes with error, you should see:
```
info: SessionJobOrchestrator[0]
      Job abc123 reached terminal state: CompletedError
info: SessionJobOrchestrator[0]
      Job abc123: Streamed 150 log lines from main log
```

Or if main log fails:
```
info: SessionJobOrchestrator[0]
      Job abc123 reached terminal state: CompletedError
fail: SessionJobOrchestrator[0]
      Job abc123: Failed to fetch job log. Status code: 404
info: SessionJobOrchestrator[0]
      Job abc123: Found 2 result files
info: SessionJobOrchestrator[0]
      Job abc123: Processing stdout file from ...
info: SessionJobOrchestrator[0]
      Job abc123: Processed 150 lines from stdout
```

## Testing Each Fallback Level

### Test 1: Main Log Available
```sas
/* Simple error that still produces main log */
data test;
    set nonexistent_dataset;
run;
```
Expected: Main log fetched successfully

### Test 2: Only Result Files Available
This depends on SLC Hub configuration. If main log fails, check application logs to see if stdout/stderr are processed.

### Test 3: No Logs at All
If this happens, you should see:
```
WARNING: No log content available for this job
```

## SLC Hub Behavior Investigation

To understand how your SLC Hub handles logs for error jobs, check:

### 1. Main Log Endpoint
```
GET /api/v2/namespaces/{ns}/jobs/{jobId}/log
```

For `CompletedError` jobs, this may:
- ✅ Return the complete log (best case)
- ❌ Return HTTP 404 (log not available)
- ❌ Return HTTP 500 (internal error)
- ⚠️ Return empty string

### 2. Results Endpoint
```
GET /api/v2/namespaces/{ns}/jobs/{jobId}/results
```

Should return list of files, commonly:
- `stdout` - Full SAS log output
- `stderr` - Error messages
- `*.sas7bdat` - Output datasets (if any)

### 3. Result File Content
```
GET {fileUrl}
```

Should return the actual file content.

## Common Scenarios

### Scenario 1: Main Log Unavailable, stdout Has Full Log
```
CompletedError → Main log fails → stdout contains complete SAS log → Display stdout
```
**Result:** User sees all log lines

### Scenario 2: Main Log Has Partial Content, stderr Has Errors
```
CompletedError → Main log has partial output → stderr has ERROR lines → Display both
```
**Result:** User sees main log + additional errors from stderr

### Scenario 3: All Sources Available
```
CompletedError → Main log OK → stdout OK → stderr OK
```
**Result:** User sees main log, with deduplicated additions from stdout/stderr

## Client-Side Behavior

The client receives log lines via SignalR `ReceiveLog` events, regardless of source:

```javascript
connection.on("ReceiveLog", (line) => {
    // Append line to log display
    appendLogLine(line);
});
```

The client doesn't need to know whether logs came from:
- Main log endpoint
- stdout result file
- stderr result file

## Configuration

No configuration changes needed. The fallback logic is automatic.

## Performance Considerations

### Multiple File Fetches
For `CompletedError` jobs, the application may fetch:
1. Main log (may fail)
2. Results list
3. stdout file content
4. stderr file content

This is 3-4 HTTP calls vs. 1 for successful jobs.

### Deduplication
The code checks `if (!logLines.Contains(line))` to avoid duplicate lines when processing multiple sources. For very large logs, this could be slow. Consider using a `HashSet<string>` if performance is an issue.

## Next Steps

1. **Run the updated application**
2. **Submit code that causes errors**
3. **Check if logs now appear**
4. **Review application logs** to see which fallback was used
5. **Report any issues** with specific log messages

## Summary

✅ **Fallback log retrieval** - Tries main log, then stdout, then stderr
✅ **Detailed logging** - Shows exactly what's happening at each step
✅ **Error resilience** - Continues even if one source fails
✅ **Duplicate prevention** - Avoids showing same line twice
✅ **User feedback** - Shows warnings if no logs available

The application should now display logs for both `CompletedSuccess` and `CompletedError` jobs!
