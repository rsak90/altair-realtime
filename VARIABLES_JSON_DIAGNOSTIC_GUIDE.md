# Variables.json Diagnostic Guide

## Overview

I've added enhanced logging to help diagnose why `variables.json` is not being created. This guide will help you trace through the flow and identify where the process is breaking.

## Enhanced Logging Added

### 1. SessionJobOrchestrator.StreamAndFinalizeAsync (Line ~196-206)
```csharp
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
```

### 2. MacroVarStore.SetAsync (Line ~68-79)
```csharp
_logger.LogDebug("SetAsync called for session {SessionId} with {Count} variables", sessionId, vars.Count);

// After userId resolution
_logger.LogDebug("SetAsync: userId resolved to {UserId} for session {SessionId}, initiating file write", userId, sessionId);

// Or if userId is null
_logger.LogWarning("Skipping file write for session {SessionId}: userId could not be resolved. Macro variables will remain in memory only.", sessionId);
```

### 3. MacroVarStore.WriteToFileAsync (Line ~247-251)
```csharp
_logger.LogDebug("WriteToFileAsync called for session {SessionId}, user {UserId} with {Count} variables", 
    sessionId, userId, variables.Count);
```

### 4. Existing Success Log (Line ~289)
```csharp
_logger.LogInformation(
    "Successfully wrote {VariableCount} macro variables for session {SessionId} (user {UserId}) to {FilePath}",
    variables.Count, sessionId, userId, filePath);
```

## Diagnostic Steps

### Step 1: Verify Configuration is Loaded

**Check on Application Startup:**

✅ **Expected Log:**
```
MacroVarStore initialized with StudyFolder: /sas/studies/development. Macro variable persistence is enabled.
```

❌ **If you see this instead:**
```
SessionStorage:StudyFolder configuration is missing. MacroVarStore will operate in in-memory-only mode without persistence.
```
**Action:** Configuration is not being loaded. Check that you're running in Development mode or that production config has the setting.

---

### Step 2: Submit a Job with Macro Variables

**Submit this SAS code:**
```sas
%let myvar = test123;
%let another = value456;
%put NOTE: Setting variables;
%put _user_;
```

**Important:** The code must include `%put _user_;` for the macro variables to appear in the log output.

---

### Step 3: Check Macro Variable Parsing

**After Job Completion, Look for:**

✅ **Expected Log (Information level):**
```
Job {jobId}: Parsed 2 macro variables from logs
```

❌ **If you see:**
```
Job {jobId}: Parsed 0 macro variables from logs
Job {jobId}: No macro variables to persist for session {sessionId}
```

**Diagnosis:** Macro variables are not being parsed from the log.

**Possible Causes:**
1. The `%put _user_;` statement is not in your SAS code
2. The SAS job is failing before reaching `%put _user_;`
3. The log output is not being captured properly
4. The log parser regex is not matching the output format

**Action:** Check the actual log content to verify the `%put _user_;` output appears.

---

### Step 4: Check SetAsync Invocation

**Look for (Debug level):**

✅ **Expected Log:**
```
Job {jobId}: Calling SetAsync to persist 2 variables for session {sessionId}
SetAsync called for session {sessionId} with 2 variables
```

❌ **If missing:**
**Diagnosis:** `SetAsync` is not being called because no variables were parsed.
**Action:** Go back to Step 3.

---

### Step 5: Check UserId Resolution

**Look for (Debug level):**

✅ **Expected Log:**
```
SetAsync: userId resolved to {userId} for session {sessionId}, initiating file write
```

❌ **If you see (Warning level):**
```
Skipping file write for session {sessionId}: userId could not be resolved. Macro variables will remain in memory only.
```

**Diagnosis:** UserId resolution is failing.

**Possible Causes:**
1. `RegisterSession()` was not called before `SetAsync()`
2. The sessionId mapping was not cached in `_sessionToUser`
3. The session directory doesn't exist for filesystem scanning fallback

**Action to Verify:**

Check earlier logs for:
```
Registered session {sessionId} for user {userId}
```

If missing, `RegisterSession()` is not being called or the cast to concrete `MacroVarStore` is failing.

---

### Step 6: Check WriteToFileAsync Invocation

**Look for (Debug level):**

✅ **Expected Log:**
```
WriteToFileAsync called for session {sessionId}, user {userId} with 2 variables
```

❌ **If missing:**
**Diagnosis:** The background task to write the file was never started.
**Action:** Check Step 5 - userId must be resolved for WriteToFileAsync to be called.

---

### Step 7: Check File Write Success

**Look for (Information level):**

✅ **Expected Log:**
```
Successfully wrote 2 macro variables for session {sessionId} (user {userId}) to {FilePath}
```

**File should exist at:** `{StudyFolder}/sessions/{userId}/{sessionId}/variables.json`

❌ **If you see (Warning level):**
```
I/O error when writing variables file for session {sessionId} (user {userId}) at {FilePath}: {Message}
```
**Or:**
```
Access denied when writing variables file for session {sessionId} (user {UserId}) at {FilePath}
```

**Diagnosis:** File write is failing due to filesystem issues.

**Possible Causes:**
1. Directory doesn't exist (should be auto-created, but might fail)
2. Insufficient permissions
3. Disk full or readonly filesystem
4. Path is invalid (e.g., Windows vs Unix path separators)

---

## Common Scenarios

### Scenario A: No Variables Parsed (Count = 0)

**Symptoms:**
```
Job {jobId}: Parsed 0 macro variables from logs
Job {jobId}: No macro variables to persist for session {sessionId}
```

**Root Cause:** The `%put _user_;` output is not being captured in the log.

**Solutions:**
1. Ensure `%put _user_;` is in your SAS code
2. Check that the PreambleBuilder is adding `%put _user_;` to the code (it should)
3. Verify the log is being fetched from the SLC Hub correctly
4. Check if the job is completing successfully

---

### Scenario B: UserId Cannot Be Resolved

**Symptoms:**
```
SetAsync called for session {sessionId} with 2 variables
Skipping file write for session {sessionId}: userId could not be resolved
```

**Root Cause:** The sessionId-to-userId mapping is not established.

**Solutions:**
1. Verify `RegisterSession()` is being called in `SessionJobOrchestrator.SubmitAsync()`
2. Check if `macroVarStore is MacroVarStore` cast is successful
3. Ensure `SessionJobOrchestrator` is injecting `IMacroVarStore` as `MacroVarStore` (not a mock or wrapper)

---

### Scenario C: File Write Silently Failing

**Symptoms:**
```
WriteToFileAsync called for session {sessionId}, user {userId} with 2 variables
[No success or error log after this]
```

**Root Cause:** Background task is failing silently (though it should log warnings).

**Solutions:**
1. Check application logs for unhandled exceptions
2. Verify StudyFolder path is accessible
3. Check filesystem permissions
4. Try a different StudyFolder path

---

### Scenario D: Path Issues (Windows)

**Symptoms:**
```
I/O error when writing variables file for session {sessionId} (user {userId}) at {FilePath}
```

**Root Cause:** Path separators or invalid characters.

**For Windows, ensure:**
```json
{
  "SessionStorage": {
    "StudyFolder": "C:\\SASData\\Studies\\Development"
  }
}
```

**Or use forward slashes (usually works):**
```json
{
  "SessionStorage": {
    "StudyFolder": "C:/SASData/Studies/Development"
  }
}
```

---

## Log Level Configuration

To see all diagnostic logs, set log level to Debug in `appsettings.Development.json`:

```json
{
  "Logging": {
    "LogLevel": {
      "Default": "Debug",
      "Microsoft.AspNetCore": "Information",
      "SasJobRunner.Services.MacroVarStore": "Debug",
      "SasJobRunner.Services.SessionJobOrchestrator": "Debug"
    }
  }
}
```

---

## Quick Test Script

```sas
/* Test macro variable persistence */
%let testvar1 = HelloWorld;
%let testvar2 = Value123;
%let testvar3 = Another_Value;

%put NOTE: === Testing Macro Variable Persistence ===;
%put NOTE: testvar1 = &testvar1;
%put NOTE: testvar2 = &testvar2;
%put NOTE: testvar3 = &testvar3;

/* This is critical - triggers the _user_ output that gets parsed */
%put _user_;

%put NOTE: === End of Test ===;
```

---

## Expected Full Log Sequence

When everything works correctly, you should see this sequence:

```
1. MacroVarStore initialized with StudyFolder: /sas/studies/development. Macro variable persistence is enabled.
2. Creating execution folder: /sas/studies/development/sessions/{userId}/{sessionId}
3. Registered session {sessionId} for user {userId}
4. Job {jobId} reached terminal state: CompletedSuccess
5. Job {jobId}: Streamed {count} log lines from main log
6. Job {jobId}: Parsed 3 macro variables from logs
7. Job {jobId}: Calling SetAsync to persist 3 variables for session {sessionId}
8. SetAsync called for session {sessionId} with 3 variables
9. SetAsync: userId resolved to {userId} for session {sessionId}, initiating file write
10. WriteToFileAsync called for session {sessionId}, user {userId} with 3 variables
11. Created session directory: {directory}
12. Successfully wrote 3 macro variables for session {sessionId} (user {userId}) to {filePath}
```

---

## Next Steps

1. **Restart the application** with the enhanced logging
2. **Submit a test job** with the script above
3. **Check the logs** and follow the diagnostic steps
4. **Report which step is failing** so we can investigate further

The enhanced logging will tell us exactly where the process is breaking!
