# Variables.json Not Being Created - Root Cause and Fix

## Problem Summary

The `variables.json` file was not being created to persist macro variables across application restarts, even though the persistence logic was fully implemented in `MacroVarStore`.

## Root Cause

**Missing Configuration**: The `SessionStorage:StudyFolder` configuration was missing from both `appsettings.json` and `appsettings.Development.json`.

### Why This Caused the Issue

1. **MacroVarStore Constructor** (line 25):
   ```csharp
   _studyFolder = _configuration["SessionStorage:StudyFolder"];
   ```
   Without the configuration, `_studyFolder` is `null`.

2. **Early Exit in WriteToFileAsync** (lines 247-251):
   ```csharp
   if (string.IsNullOrWhiteSpace(_studyFolder))
   {
       _logger.LogDebug("Skipping file write for session {SessionId}: StudyFolder is not configured", sessionId);
       return;
   }
   ```
   When `_studyFolder` is null, the method exits early and **never writes the file**.

3. **In-Memory-Only Mode** (lines 28-31):
   ```csharp
   if (string.IsNullOrWhiteSpace(_studyFolder))
   {
       _logger.LogError("SessionStorage:StudyFolder configuration is missing. MacroVarStore will operate in in-memory-only mode without persistence.");
       _logger.LogWarning("Macro variables will not persist across application restarts. Configure SessionStorage:StudyFolder to enable persistence.");
   }
   ```
   The store falls back to in-memory-only mode.

## The Fix

Added the missing `SessionStorage:StudyFolder` configuration to both configuration files:

### appsettings.json (Production)
```json
{
  "SessionStorage": {
    "StudyFolder": "/sas/studies/production"
  }
}
```

### appsettings.Development.json (Development)
```json
{
  "SessionStorage": {
    "StudyFolder": "/sas/studies/development"
  }
}
```

## How It Works Now

With the configuration in place:

1. **On Job Submission**:
   - `SessionJobOrchestrator.SubmitAsync()` calls `RegisterSession(sessionId, userId)` (line 31)
   - This caches the sessionId-to-userId mapping in `_sessionToUser`

2. **After Job Completion** (in `StreamAndFinalizeAsync`):
   - Log parser extracts macro variables from job output
   - `macroVarStore.SetAsync(sessionId, newVars)` is called (line 195)

3. **In SetAsync** (MacroVarStore line 70-90):
   - Updates in-memory cache immediately
   - Resolves userId from cache (already registered)
   - Fires background task to write to file

4. **In WriteToFileAsync**:
   - ✅ `_studyFolder` is now set, so early exit doesn't happen
   - Creates session directory: `{StudyFolder}/sessions/{userId}/{sessionId}/`
   - Writes atomic file: `variables.{guid}.tmp` → `variables.json`
   - Logs success: `"Successfully wrote {VariableCount} macro variables..."`

5. **File Location**:
   ```
   /sas/studies/development/sessions/{userId}/{sessionId}/variables.json
   ```

## Expected File Structure

After a job with macro variables completes, you'll see:

```json
{
  "metadata": {
    "userId": "user@example.com",
    "lastUpdated": "2026-06-17T10:30:00Z"
  },
  "variables": {
    "VAR1": "value1",
    "VAR2": "value2"
  }
}
```

## Verification Steps

1. **Check Logs on Startup**:
   - ✅ Should see: `"MacroVarStore initialized with StudyFolder: /sas/studies/development. Macro variable persistence is enabled."`
   - ❌ Should NOT see: `"SessionStorage:StudyFolder configuration is missing. MacroVarStore will operate in in-memory-only mode"`

2. **Submit a Job with Macro Variables**:
   ```sas
   %let myvar = test123;
   %let another = value456;
   ```

3. **Check Logs After Job Completion**:
   - Should see: `"Successfully wrote 2 macro variables for session {sessionId}..."`

4. **Verify File Exists**:
   ```
   /sas/studies/development/sessions/{userId}/{sessionId}/variables.json
   ```

5. **Test Persistence**:
   - Restart the application
   - Submit another job with the same sessionId
   - Variables should be loaded from file on first access

## Related Services

Other services also require this configuration:
- ✅ `PreambleBuilder` - Constructs SESSLIB path
- ✅ `SessionJobOrchestrator` - Creates execution folder
- ✅ `DatasetReaderService` - Reads dataset files
- ✅ `FilesApiController` - Lists session files

All these services will now work correctly with the configuration in place.

## Configuration Options

You can customize the StudyFolder path based on your environment:

- **Development**: `/sas/studies/development`
- **Staging**: `/sas/studies/staging`
- **Production**: `/sas/studies/production`

Or for Windows:
```json
{
  "SessionStorage": {
    "StudyFolder": "C:\\SASData\\Studies\\Development"
  }
}
```

## Summary

✅ **Root Cause**: Missing `SessionStorage:StudyFolder` configuration
✅ **Fix**: Added configuration to both appsettings files  
✅ **Result**: `variables.json` files will now be created and persist macro variables
✅ **Location**: `{StudyFolder}/sessions/{userId}/{sessionId}/variables.json`

The implementation was already complete - it just needed the configuration to activate the persistence feature!
