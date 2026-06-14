# File API 500 Error - Diagnosis and Fix

## Problem
The `/api/files` endpoint was returning a **500 Internal Server Error**, preventing the file browser from loading dataset files.

## Root Cause
The error had multiple potential causes:

1. **Session Not Initialized** - The `FilesApiController.ListFiles()` requires both `UserId` and `SessionId` to be present in the session, but:
   - `UserId` is only set by `TokenManager.EnsureValidTokenAsync()` after successful authentication
   - If authentication fails, the session remains empty
   - The error message wasn't clear about what failed

2. **Poor Error Handling** - The original code:
   - Threw exceptions that resulted in generic 500 errors
   - Didn't log enough diagnostic information
   - Returned simple string error messages instead of structured JSON

3. **Hardcoded Paths** - Some controllers were using hardcoded `/sas/sessions/` paths instead of the configured `SessionStorage:StudyFolder`

## Changes Made

### 1. FilesApiController.cs - Enhanced Error Handling
- Added comprehensive logging at each step
- Wrapped entire method in try-catch to prevent unhandled exceptions
- Return structured JSON error responses with `error` and `detail` fields
- Log UserId, SessionId, and working directory paths for diagnostics
- Check configuration existence before using it

### 2. EditorController.cs - Use Configured Paths
- Inject `IConfiguration` and `ILogger` dependencies
- Use `SessionStorage:StudyFolder` configuration instead of hardcoded paths
- Add logging when session is created
- Handle directory creation errors gracefully

### 3. SessionController.cs - Use Configured Paths
- Inject `IConfiguration` and `ILogger` dependencies
- Use `SessionStorage:StudyFolder` configuration instead of hardcoded paths
- Add logging for session creation
- Handle directory creation errors gracefully

## How to Diagnose
After rebuilding and running the application, check the logs for:

```
ListFiles called - UserId: (null), SessionId: (null)
```
This indicates authentication failed - check SlcHub configuration and credentials.

```
ListFiles called - UserId: user@example.com, SessionId: abc-123
Checking working directory: //filer/home/study1/datasets/sessions/user@example.com/abc-123
```
This indicates authentication succeeded - check if the path exists and is accessible.

## Next Steps
1. **Rebuild the application**: `dotnet build` (already completed ✓)
2. **Run the application**: `dotnet run`
3. **Check the browser console** - Error messages should now be more descriptive
4. **Check application logs** - Look for the diagnostic messages added
5. **Verify authentication** - Ensure SlcHub:ServiceAccount credentials are correct in appsettings.Development.json
6. **Verify file paths** - Ensure the StudyFolder path is accessible from the application

## Configuration Checklist
Verify these settings in `appsettings.Development.json`:

```json
{
  "SlcHub": {
    "BaseUrl": "http://localhost:8080",  // ← Must be accessible
    "ServiceAccount": {
      "Username": "your-service-username",  // ← Must be valid
      "Password": "your-service-password"   // ← Must be valid
    },
    "UserId": "your-user-id"  // ← The user to impersonate
  },
  "SessionStorage": {
    "StudyFolder": "//filer/home/study1/datasets"  // ← Must be accessible
  }
}
```

## Testing
Once the application is running:
1. Navigate to `/Editor`
2. Check browser console (F12) for any errors
3. Look at the network tab to see the actual error response from `/api/files`
4. Check application logs for diagnostic messages
