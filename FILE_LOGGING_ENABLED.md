# File Logging Enabled

## What I Did

I've configured Serilog for comprehensive file-based logging so you can see all application logs.

### Changes Made:

1. **Added Serilog packages** to `SasJobRunner.csproj`:
   - `Serilog.AspNetCore` (v8.0.0)
   - `Serilog.Sinks.File` (v5.0.0)

2. **Configured Serilog** in `Program.cs`:
   - Logs to both console and file
   - Log level: Debug (captures everything)
   - File path: `logs/sasjobrunner-YYYYMMDD.log`
   - Rolling interval: Daily (new file each day)

3. **Enhanced logging** in:
   - `SessionJobOrchestrator.cs` - Shows complete log content and parsing results
   - `MacroVarStore.cs` - Shows SetAsync and WriteToFileAsync calls
   - `LogParserService.cs` - Shows console output of parsing

4. **Created .gitignore** to exclude log files from git

## How to Use

### Step 1: Restore Packages and Build
```bash
cd SasJobRunner
dotnet restore
dotnet build
```

### Step 2: Start the Application
```bash
dotnet run
```

### Step 3: Check Log Files

Logs will be written to:
```
c:\Users\USER\altair-realtime\SasJobRunner\logs\sasjobrunner-YYYYMMDD.log
```

Example: `sasjobrunner-20260618.log`

### Step 4: Run a Test Job

In the editor, type this SAS code:
```sas
%let myvar = test123;
%let another = value456;
%put NOTE: Testing variables;
```

Then click Run/Submit.

### Step 5: Review the Log File

Open the log file in the `logs/` folder. You should see:

#### Section 1: Complete Log Content
```
Job {jobId}: Complete log content (50 lines):
==================== START LOG ====================
NOTE: PROCEDURE SAS (V9M8) executed.
...
SESSIONID=abc123
MYVAR=test123
ANOTHER=value456
...
==================== END LOG ====================
```

#### Section 2: Parsing Results
```
Job {jobId}: Parsed 2 macro variables from 50 log lines
Job {jobId}: Parsed variables:
  MYVAR = test123
  ANOTHER = value456
```

#### Section 3: SetAsync Call
```
SetAsync called for session {sessionId} with 2 variables
SetAsync: userId resolved to {userId} for session {sessionId}, initiating file write
```

#### Section 4: File Write
```
WriteToFileAsync called for session {sessionId}, user {userId} with 2 variables
Successfully wrote 2 macro variables for session {sessionId} (user {userId}) to {FilePath}
```

## What to Look For

### ✅ Success Case

You should see all four sections above, ending with:
```
Successfully wrote 2 macro variables for session {sessionId} (user {userId}) to /sas/studies/development/sessions/{userId}/{sessionId}/variables.json
```

Then check if the file exists at that path.

### ❌ Failure Case 1: No Variables Parsed

If you see:
```
Job {jobId}: Parsed 0 macro variables from 50 log lines
Job {jobId}: No variables parsed. First 5 log lines:
  >>> NOTE: ...
  >>> NOTE: ...
Job {jobId}: Last 5 log lines:
  >>> NOTE: ...
  >>> NOTE: ...
```

**Problem:** The log doesn't contain `SESSIONID=` line or the format is different.

**Action:** Look at the complete log content between the START/END markers and search for:
- Is there a line starting with `SESSIONID=`?
- Are there lines like `MYVAR=test123`?
- Or are they prefixed like `NOTE: SESSIONID=abc123`?

### ❌ Failure Case 2: UserId Not Resolved

If you see:
```
Job {jobId}: Parsed 2 macro variables from 50 log lines
SetAsync called for session {sessionId} with 2 variables
Skipping file write for session {sessionId}: userId could not be resolved
```

**Problem:** The sessionId-to-userId mapping is not working.

**Action:** Look earlier in the log for:
- `Registered session {sessionId} for user {userId}` - should appear when job is submitted
- If missing, there's an issue with `RegisterSession()` not being called

### ❌ Failure Case 3: File Write Failed

If you see:
```
WriteToFileAsync called for session {sessionId}, user {userId} with 2 variables
I/O error when writing variables file for session {sessionId} (user {userId}) at {FilePath}
```

**Problem:** File system error (permissions, invalid path, etc.)

**Action:** Check the file path in the error message and verify:
- Does the directory exist? (Should be auto-created)
- Do you have write permissions?
- Is the path valid for Windows?

## Console Output

You'll also see console output from the parser:
```
[LogParser] Found SESSIONID= line at line 42: SESSIONID=abc123
[LogParser] Parsed variable: MYVAR = test123
[LogParser] Parsed variable: ANOTHER = value456
[LogParser] Summary: Total lines=50, Skipped=2, Parsed=2, Result count=2
```

## Troubleshooting

### Log File Not Created

If the `logs/` folder or log file is not created:
1. Check you're running from the correct directory
2. Ensure you ran `dotnet restore` to install Serilog packages
3. Check for startup errors in the console

### Log File Empty

If the file exists but is empty:
1. Wait a few seconds - Serilog buffers writes
2. Check console for errors
3. Try stopping and restarting the application

### Can't Find the Log File

The log file will be at:
```
{ProjectRoot}/SasJobRunner/logs/sasjobrunner-{date}.log
```

Full path:
```
c:\Users\USER\altair-realtime\SasJobRunner\logs\sasjobrunner-20260618.log
```

## Next Steps

1. **Build and run** the application
2. **Submit a test job** with macro variables
3. **Open the log file** in `SasJobRunner/logs/`
4. **Search for these patterns:**
   - `==================== START LOG ====================`
   - `Parsed X macro variables`
   - `Successfully wrote`
5. **Share the relevant log sections** if variables.json is still not created

The log file will show us exactly what's happening at each step! 📝
