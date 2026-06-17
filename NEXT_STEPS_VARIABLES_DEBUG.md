# Next Steps: Variables.json Debug

## What I've Done

Since you confirmed that `_studyFolder` has a value, the configuration is correctly loaded. I've added **enhanced diagnostic logging** throughout the flow to help identify where the process is breaking.

## Changes Made

### 1. SessionJobOrchestrator.cs
- ✅ Added logging to show how many variables were parsed
- ✅ Added logging when `SetAsync` is called
- ✅ Added logging when no variables are found

### 2. MacroVarStore.cs
- ✅ Added logging when `SetAsync` is called with variable count
- ✅ Added logging when userId is successfully resolved
- ✅ Added logging when `WriteToFileAsync` is called

## The Flow to Watch

```
┌─────────────────────────────────────────────────────────────┐
│ 1. Job Submission                                           │
│    ✓ RegisterSession(sessionId, userId)                     │
│    ✓ Create execution folder                                │
└─────────────────────────────────────────────────────────────┘
                           ↓
┌─────────────────────────────────────────────────────────────┐
│ 2. Job Completion                                           │
│    → Parse logs for macro variables                         │
│    → Log: "Parsed {Count} macro variables"                  │
└─────────────────────────────────────────────────────────────┘
                           ↓
┌─────────────────────────────────────────────────────────────┐
│ 3. IF Count > 0                                             │
│    → Call SetAsync(sessionId, vars)                         │
│    → Log: "Calling SetAsync to persist {Count} variables"   │
└─────────────────────────────────────────────────────────────┘
                           ↓
┌─────────────────────────────────────────────────────────────┐
│ 4. SetAsync                                                 │
│    → Update in-memory cache                                 │
│    → Log: "SetAsync called for session {sessionId}"         │
│    → Resolve userId                                         │
└─────────────────────────────────────────────────────────────┘
                           ↓
┌─────────────────────────────────────────────────────────────┐
│ 5. IF userId != null                                        │
│    → Start background task                                  │
│    → Log: "userId resolved to {userId}, initiating write"   │
│    → Call WriteToFileAsync                                  │
└─────────────────────────────────────────────────────────────┘
                           ↓
┌─────────────────────────────────────────────────────────────┐
│ 6. WriteToFileAsync                                         │
│    → Log: "WriteToFileAsync called for session..."          │
│    → Create directory if needed                             │
│    → Write temp file                                        │
│    → Rename to variables.json                               │
│    → Log: "Successfully wrote {Count} macro variables..."   │
└─────────────────────────────────────────────────────────────┘
```

## What to Do Next

### Step 1: Rebuild and Restart
```bash
cd SasJobRunner
dotnet build
dotnet run
```

### Step 2: Submit a Test Job

Use this SAS code:
```sas
%let myvar = test123;
%let another = value456;
%put NOTE: Testing variable persistence;
%put _user_;
```

**IMPORTANT:** The `%put _user_;` statement is critical!

### Step 3: Watch the Logs

Look for this sequence in your application logs:

#### 3a. At Startup
```
MacroVarStore initialized with StudyFolder: /sas/studies/development. Macro variable persistence is enabled.
```

#### 3b. When Job is Submitted
```
Creating execution folder: {path}
Registered session {sessionId} for user {userId}
```

#### 3c. After Job Completes
```
Job {jobId}: Parsed {N} macro variables from logs
Job {jobId}: Calling SetAsync to persist {N} variables for session {sessionId}
SetAsync called for session {sessionId} with {N} variables
SetAsync: userId resolved to {userId} for session {sessionId}, initiating file write
WriteToFileAsync called for session {sessionId}, user {userId} with {N} variables
Successfully wrote {N} macro variables for session {sessionId} (user {userId}) to {FilePath}
```

### Step 4: Identify Where It Breaks

**Tell me which log message is the LAST one you see from the sequence above.**

This will tell us exactly where the process is breaking:

- ❌ **Stop at "Parsed 0 macro variables"** → Log parsing issue
- ❌ **Stop at "SetAsync called"** → UserId resolution issue  
- ❌ **Stop at "WriteToFileAsync called"** → File write issue
- ❌ **No "Successfully wrote"** → File system error (check for warning logs)

## Possible Issues Based on Your Environment

### Issue 1: No Macro Variables Parsed (Count = 0)

**Cause:** The `%put _user_;` output is not in the job log.

**Check:**
1. Is `%put _user_;` in your SAS code?
2. Does the job complete successfully?
3. Is the log being captured?

### Issue 2: UserId Cannot Be Resolved

**Cause:** `RegisterSession()` is not being called or failing.

**Check:**
1. Look for log: `"Registered session {sessionId} for user {userId}"`
2. If missing, the cast `if (macroVarStore is MacroVarStore concreteStore)` might be failing
3. Check DI registration - is `IMacroVarStore` registered as `MacroVarStore`?

### Issue 3: File Write Failing

**Cause:** Filesystem permissions or path issues.

**Check:**
1. Does the path exist? It should be auto-created
2. Do you have write permissions?
3. Is the path valid for your OS?

For Windows, use:
```json
"StudyFolder": "C:\\SASData\\Studies\\Development"
```
or
```json
"StudyFolder": "C:/SASData/Studies/Development"
```

## Reference Documents

I've created two detailed guides:

1. **VARIABLES_JSON_FIX.md** - Explains the original configuration issue
2. **VARIABLES_JSON_DIAGNOSTIC_GUIDE.md** - Complete diagnostic guide with all scenarios

## What I Need From You

After you run the test:

1. **Copy the relevant log lines** (the sequence from Step 3c above)
2. **Tell me where the sequence stops**
3. **Share any warning or error logs** you see

This will help me pinpoint the exact issue!

## Quick Checklist

- [ ] Configuration has `SessionStorage:StudyFolder` value
- [ ] Application rebuilt and restarted
- [ ] Test job submitted with `%put _user_;`
- [ ] Logs checked for the sequence above
- [ ] Identified which step fails

Let me know what you find! 🔍
