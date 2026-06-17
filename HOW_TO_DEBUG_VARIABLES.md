# How to Debug Variables.json Issue

## Quick Steps

### 1. Build and Run
```bash
cd SasJobRunner
dotnet restore
dotnet build
dotnet run
```

### 2. Submit Test Code

In the web editor, type:
```sas
%let myvar = test123;
%let another = value456;
```

Click Submit/Run.

### 3. Check the Log File

**Location:** `SasJobRunner\logs\sasjobrunner-{today}.log`

Example: `SasJobRunner\logs\sasjobrunner-20260618.log`

### 4. Search for These Patterns

Open the log file in a text editor and search for:

#### Pattern 1: "START LOG"
```
==================== START LOG ====================
```
This shows the complete SAS log content.

#### Pattern 2: "Parsed X macro variables"
```
Job {jobId}: Parsed 2 macro variables from 50 log lines
```
- If it says "Parsed 0", the log parsing failed
- Look at the log content between START/END markers

#### Pattern 3: "Successfully wrote"
```
Successfully wrote 2 macro variables for session {sessionId} (user {userId}) to {FilePath}
```
- If you see this, the file SHOULD exist at that path
- Copy the full path and check if the file exists

## What the Log Will Tell You

### Scenario A: Log Parsing Failed (Parsed 0)

**You'll see:**
```
Job {jobId}: Parsed 0 macro variables from 50 log lines
Job {jobId}: No variables parsed. First 5 log lines:
  >>> NOTE: ...
```

**Look for in the complete log:**
- Is there a line `SESSIONID=abc123` (no NOTE: prefix)?
- Are there lines like `MYVAR=test123` (no NOTE: prefix)?
- Or do they have prefixes like `NOTE: SESSIONID=abc123`?

**If prefixed:** The SAS output format is different than expected. We need to update the parser.

### Scenario B: UserId Not Resolved

**You'll see:**
```
Parsed 2 macro variables
Skipping file write for session {sessionId}: userId could not be resolved
```

**Look earlier for:**
- `Registered session {sessionId} for user {userId}` - should be there when job starts

**If missing:** The registration isn't happening. There's an issue with the orchestrator flow.

### Scenario C: File Write Failed

**You'll see:**
```
WriteToFileAsync called for session {sessionId}
I/O error when writing variables file
```

**Check:**
- The file path in the error
- Whether you have write permissions
- Whether the path is valid

### Scenario D: Everything Looks Good But No File

**You'll see:**
```
Successfully wrote 2 macro variables ... to /sas/studies/development/sessions/{userId}/{sessionId}/variables.json
```

**But file doesn't exist?**
- Copy the EXACT path from the log
- Check if it's a Windows vs Unix path issue
- Check if the application is running in a container or different filesystem

## Most Likely Issues

### Issue 1: SAS Log Format Different

The parser expects:
```
SESSIONID=abc123
MYVAR=test123
```

But SAS might be outputting:
```
NOTE: SESSIONID=abc123
NOTE: MYVAR=test123
```

**Fix:** Update the parser to handle NOTE: prefix.

### Issue 2: %put _user_; Not Executing

The SAS code might be failing before `%put _user_;` runs.

**Check:** Look for ERROR lines in the complete log content.

### Issue 3: Log Not Captured

The log fetching from SLC Hub might not be working.

**Check:** Look at the line count - if it says "0 log lines", nothing is being captured.

## Share With Me

After you check the log file, share:

1. **The line:** `Job {jobId}: Parsed X macro variables from Y log lines`
   - What is X? (how many parsed)
   - What is Y? (how many total lines)

2. **If X = 0:** Share a few lines from between START LOG and END LOG
   - Especially look for lines with `SESSIONID` or your variable names

3. **If X > 0:** Share the "Successfully wrote" line or any error that follows

This will tell me exactly what's wrong! 🎯
