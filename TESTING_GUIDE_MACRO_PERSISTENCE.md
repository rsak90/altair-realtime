# Testing Guide: Macro Variable Persistence

## Overview

This guide tests the complete macro variable persistence feature across multiple SAS job submissions to verify that variables are:
1. Captured from job logs
2. Saved to `variables.json` files
3. Loaded on subsequent submissions
4. Reloaded after application restart

## Prerequisites

1. **Build and run the application:**
   ```bash
   cd SasJobRunner
   dotnet build
   dotnet run
   ```

2. **Open the web interface** in your browser

3. **Have the log file open** for monitoring:
   ```
   SasJobRunner\logs\sasjobrunner-{today}.log
   ```
   (e.g., `sasjobrunner-20260618.log`)

---

## Test 1: Create and Persist Variables (First Submission)

### Goal
Verify that macro variables are captured from the first job and saved to `variables.json`.

### How Storage and Retrieval Works

```
┌─────────────────────────────────────────────────────────────────┐
│ REQUEST 1: First Job Submission                                 │
└─────────────────────────────────────────────────────────────────┘

1. User clicks "Submit" → POST /api/session-job/submit
   ├─ SessionJobOrchestrator.SubmitAsync() called
   │
   ├─ [RETRIEVAL ATTEMPT - Cache Miss]
   │  └─ MacroVarStore.GetAsync(sessionId)
   │     ├─ Check in-memory cache: EMPTY (first request)
   │     ├─ Check if file loaded flag: FALSE
   │     ├─ Try LoadFromFileAsync(sessionId)
   │     │  ├─ Resolve userId: SUCCESS (from RegisterSession)
   │     │  ├─ Check if file exists: NO (first request)
   │     │  └─ Return: {} (empty dictionary)
   │     └─ Store empty dict in cache
   │
   ├─ Build preamble with NO variables (empty dictionary)
   │  LIBNAME SESSLIB "...";
   │  %let SESSIONID = "17e17538-...";
   │  [No other %let statements - no variables yet]
   │
   ├─ Append user code + %put _user_;
   ├─ Submit to Altair SLC
   └─ Return jobId to UI

2. Job executes on Altair SLC
   ├─ Runs user code: %let project = ClinicalTrial2024; etc.
   └─ Executes: %put _user_;
      Output:
      GLOBAL SESSIONID 17e17538-...
      GLOBAL PROJECT ClinicalTrial2024
      GLOBAL PHASE Phase3
      GLOBAL PATIENT_COUNT 150

3. Background task: StreamAndFinalizeAsync()
   ├─ Fetch job logs from Altair
   │
   ├─ [PARSING]
   │  └─ LogParserService.ParseUserMacroVars(logLines)
   │     ├─ Find: "GLOBAL SESSIONID ..." → Start block
   │     ├─ Parse: SESSIONID = 17e17538-...
   │     ├─ Parse: PROJECT = ClinicalTrial2024
   │     ├─ Parse: PHASE = Phase3
   │     ├─ Parse: PATIENT_COUNT = 150
   │     └─ Return: Dictionary with 4 variables
   │
   ├─ [STORAGE - Cache + File]
   │  └─ MacroVarStore.SetAsync(sessionId, variables)
   │     ├─ [CACHE WRITE - Synchronous]
   │     │  └─ _store[sessionId] = variables (in-memory)
   │     │     Now cache contains: 4 variables
   │     │
   │     ├─ Resolve userId from cache: SUCCESS
   │     │
   │     └─ [FILE WRITE - Async Background]
   │        └─ Task.Run(WriteToFileAsync)
   │           ├─ Acquire file lock (per-session semaphore)
   │           ├─ Create session directory if needed
   │           ├─ Serialize to JSON:
   │           │  {
   │           │    "metadata": {
   │           │      "userId": "7d3e7eac-...",
   │           │      "lastUpdated": "2026-06-18T15:00:00Z"
   │           │    },
   │           │    "variables": {
   │           │      "SESSIONID": "17e17538-...",
   │           │      "PROJECT": "ClinicalTrial2024",
   │           │      "PHASE": "Phase3",
   │           │      "PATIENT_COUNT": "150"
   │           │    }
   │           │  }
   │           ├─ Write to temp file: variables.{guid}.tmp
   │           ├─ Atomic rename: variables.{guid}.tmp → variables.json
   │           └─ Release file lock
   │
   └─ Notify UI: Job complete

┌─────────────────────────────────────────────────────────────────┐
│ RESULT: Variables now stored in TWO places                      │
│  1. In-Memory Cache: _store[sessionId] = {4 variables}          │
│  2. Disk File: {StudyFolder}/sessions/{userId}/{sessionId}/     │
│                variables.json                                    │
└─────────────────────────────────────────────────────────────────┘
```

### Steps

1. **Open the editor** (should be blank)

2. **Submit this code:**
   ```sas
   %let project = ClinicalTrial2024;
   %let phase = Phase3;
   %let patient_count = 150;
   
   %put NOTE: Project: &project;
   %put NOTE: Phase: &phase;
   %put NOTE: Patient Count: &patient_count;
   ```

3. **Wait for job to complete**

### Expected Results

#### In the Log File
```
Job {jobId}: Parsed 4 macro variables from X log lines
Job {jobId}: Parsed variables:
  SESSIONID = 17e17538-...
  PROJECT = ClinicalTrial2024
  PHASE = Phase3
  PATIENT_COUNT = 150

SetAsync called for session {sessionId} with 4 variables
SetAsync: userId resolved to {userId}, initiating file write
WriteToFileAsync called for session {sessionId}, user {userId} with 4 variables
Successfully wrote 4 macro variables for session {sessionId} (user {userId}) to {FilePath}
```

#### File Created
Check if file exists at the path shown in the log:
```
/sas/studies/development/sessions/{userId}/{sessionId}/variables.json
```

Or on Windows:
```
C:\sas\studies\development\sessions\{userId}\{sessionId}\variables.json
```

#### File Content
```json
{
  "metadata": {
    "userId": "7d3e7eac-c362-4f22-90b4-cd8b307d6926",
    "lastUpdated": "2026-06-18T15:00:00Z"
  },
  "variables": {
    "SESSIONID": "17e17538-6a45-4e3b-bca6-29a1514a8565",
    "PROJECT": "ClinicalTrial2024",
    "PHASE": "Phase3",
    "PATIENT_COUNT": "150"
  }
}
```

✅ **Pass Criteria:** File exists with all 4 variables

---

## Test 2: Variables Persist Across Submissions (Same Session)

### Goal
Verify that variables from Test 1 are automatically included in the preamble of the next submission.

### How Storage and Retrieval Works

```
┌─────────────────────────────────────────────────────────────────┐
│ REQUEST 2: Second Job Submission (Same Session)                 │
└─────────────────────────────────────────────────────────────────┘

1. User clicks "Submit" again → POST /api/session-job/submit
   ├─ SessionJobOrchestrator.SubmitAsync() called
   │
   ├─ [RETRIEVAL - Cache Hit!]
   │  └─ MacroVarStore.GetAsync(sessionId)
   │     ├─ Check in-memory cache: FOUND! ✅
   │     │  _store[sessionId] = {
   │     │    SESSIONID: "17e17538-...",
   │     │    PROJECT: "ClinicalTrial2024",
   │     │    PHASE: "Phase3",
   │     │    PATIENT_COUNT: "150"
   │     │  }
   │     ├─ No file read needed (cache-first strategy)
   │     └─ Return: Dictionary with 4 variables INSTANTLY
   │
   ├─ Build preamble WITH variables from cache
   │  LIBNAME SESSLIB "...";
   │  %let SESSIONID = "17e17538-...";
   │  %let PROJECT = ClinicalTrial2024;     ← From cache!
   │  %let PHASE = Phase3;                  ← From cache!
   │  %let PATIENT_COUNT = 150;             ← From cache!
   │
   ├─ Append user code:
   │  %let site = Hospital_A;               ← New variable
   │  %put NOTE: Previous vars available!
   │  %put _user_;
   │
   ├─ Submit to Altair SLC
   └─ Return jobId to UI

2. Job executes on Altair SLC
   ├─ Preamble sets: PROJECT, PHASE, PATIENT_COUNT (from Request 1)
   ├─ User code defines: SITE (new)
   └─ Executes: %put _user_;
      Output:
      GLOBAL SESSIONID 17e17538-...
      GLOBAL PROJECT ClinicalTrial2024      ← From preamble
      GLOBAL PHASE Phase3                   ← From preamble
      GLOBAL PATIENT_COUNT 150              ← From preamble
      GLOBAL SITE Hospital_A                ← New!

3. Background task: StreamAndFinalizeAsync()
   ├─ Fetch job logs from Altair
   │
   ├─ [PARSING]
   │  └─ LogParserService.ParseUserMacroVars(logLines)
   │     ├─ Parse: SESSIONID = 17e17538-...
   │     ├─ Parse: PROJECT = ClinicalTrial2024
   │     ├─ Parse: PHASE = Phase3
   │     ├─ Parse: PATIENT_COUNT = 150
   │     ├─ Parse: SITE = Hospital_A        ← New!
   │     └─ Return: Dictionary with 5 variables
   │
   ├─ [STORAGE UPDATE - Cache + File]
   │  └─ MacroVarStore.SetAsync(sessionId, variables)
   │     ├─ [CACHE UPDATE - Synchronous]
   │     │  └─ _store[sessionId] = new variables (overwrite)
   │     │     Cache now contains: 5 variables
   │     │     (4 existing + 1 new)
   │     │
   │     └─ [FILE UPDATE - Async Background]
   │        └─ Task.Run(WriteToFileAsync)
   │           ├─ Acquire file lock
   │           ├─ Serialize NEW state to JSON (all 5 variables)
   │           ├─ Write to temp file: variables.{guid}.tmp
   │           ├─ Atomic rename: OVERWRITES old variables.json
   │           │  Old: 4 variables
   │           │  New: 5 variables
   │           └─ Release file lock
   │
   └─ Notify UI: Job complete

┌─────────────────────────────────────────────────────────────────┐
│ INTERACTION BETWEEN REQUESTS:                                   │
│                                                                  │
│ Request 1 → SetAsync()                                          │
│             ├─ Cache: 4 vars                                     │
│             └─ File: 4 vars                                      │
│                                                                  │
│ Request 2 → GetAsync()                                          │
│             └─ Reads from CACHE (no file I/O!)                  │
│                                                                  │
│ Request 2 → SetAsync()                                          │
│             ├─ Cache: 5 vars (updated)                          │
│             └─ File: 5 vars (overwritten)                       │
│                                                                  │
│ KEY: Cache-first strategy = Fast retrieval, no disk reads!      │
└─────────────────────────────────────────────────────────────────┘
```

### Steps

1. **In the same session** (don't close browser, don't restart app)

2. **Submit this code:**
   ```sas
   %put NOTE: === Checking Persisted Variables ===;
   %put NOTE: Project from previous run: &project;
   %put NOTE: Phase from previous run: &phase;
   %put NOTE: Patient count from previous run: &patient_count;
   
   /* Add a new variable */
   %let site = Hospital_A;
   %put NOTE: New site: &site;
   ```

### Expected Results

#### In the Job Log (UI)
```
NOTE: === Checking Persisted Variables ===
NOTE: Project from previous run: ClinicalTrial2024
NOTE: Phase from previous run: Phase3
NOTE: Patient count from previous run: 150
NOTE: New site: Hospital_A
```

✅ Variables from Test 1 are available!

#### In the Application Log File
```
Job {jobId}: Parsed 5 macro variables from X log lines
Job {jobId}: Parsed variables:
  SESSIONID = 17e17538-...
  PROJECT = ClinicalTrial2024
  PHASE = Phase3
  PATIENT_COUNT = 150
  SITE = Hospital_A
  
Successfully wrote 5 macro variables for session {sessionId} (user {userId}) to {FilePath}
```

#### Updated File Content
The `variables.json` file should now include the new variable:
```json
{
  "metadata": {
    "userId": "7d3e7eac-c362-4f22-90b4-cd8b307d6926",
    "lastUpdated": "2026-06-18T15:05:00Z"
  },
  "variables": {
    "SESSIONID": "17e17538-6a45-4e3b-bca6-29a1514a8565",
    "PROJECT": "ClinicalTrial2024",
    "PHASE": "Phase3",
    "PATIENT_COUNT": "150",
    "SITE": "Hospital_A"
  }
}
```

✅ **Pass Criteria:** 
- Previous variables are available in the new job
- File is updated with the new variable (5 total)

---

## Test 3: Variable Updates (Modify Existing Variables)

### Goal
Verify that modifying an existing variable updates its value in the file.

### How Storage and Retrieval Works

```
┌─────────────────────────────────────────────────────────────────┐
│ REQUEST 3: Third Job Submission (Update Variables)              │
└─────────────────────────────────────────────────────────────────┘

1. User clicks "Submit" → POST /api/session-job/submit
   ├─ SessionJobOrchestrator.SubmitAsync() called
   │
   ├─ [RETRIEVAL - Cache Hit]
   │  └─ MacroVarStore.GetAsync(sessionId)
   │     ├─ Check in-memory cache: FOUND! ✅
   │     │  _store[sessionId] = {5 variables from Request 2}
   │     └─ Return: Dictionary with 5 variables
   │
   ├─ Build preamble WITH all 5 variables
   │  LIBNAME SESSLIB "...";
   │  %let SESSIONID = "17e17538-...";
   │  %let PROJECT = ClinicalTrial2024;
   │  %let PHASE = Phase3;                  ← Will be updated
   │  %let PATIENT_COUNT = 150;             ← Will be updated
   │  %let SITE = Hospital_A;
   │
   ├─ Append user code:
   │  %let patient_count = 175;             ← UPDATES existing
   │  %let phase = Phase4;                  ← UPDATES existing
   │  %put _user_;
   │
   ├─ Submit to Altair SLC
   └─ Return jobId to UI

2. Job executes on Altair SLC
   ├─ Preamble sets: PATIENT_COUNT = 150, PHASE = Phase3
   ├─ User code OVERWRITES: 
   │  %let patient_count = 175;  → PATIENT_COUNT = 175
   │  %let phase = Phase4;        → PHASE = Phase4
   └─ Executes: %put _user_;
      Output (SAS uses LATEST values):
      GLOBAL SESSIONID 17e17538-...
      GLOBAL PROJECT ClinicalTrial2024      ← Unchanged
      GLOBAL PHASE Phase4                   ← UPDATED! ✅
      GLOBAL PATIENT_COUNT 175              ← UPDATED! ✅
      GLOBAL SITE Hospital_A                ← Unchanged

3. Background task: StreamAndFinalizeAsync()
   ├─ Fetch job logs from Altair
   │
   ├─ [PARSING]
   │  └─ LogParserService.ParseUserMacroVars(logLines)
   │     ├─ Parse: SESSIONID = 17e17538-...
   │     ├─ Parse: PROJECT = ClinicalTrial2024
   │     ├─ Parse: PHASE = Phase4           ← New value
   │     ├─ Parse: PATIENT_COUNT = 175      ← New value
   │     ├─ Parse: SITE = Hospital_A
   │     └─ Return: Dictionary with 5 variables (2 updated)
   │
   ├─ [STORAGE UPDATE - Cache + File]
   │  └─ MacroVarStore.SetAsync(sessionId, variables)
   │     ├─ [CACHE UPDATE - Synchronous]
   │     │  └─ _store[sessionId] = new variables
   │     │     Before: {PHASE: "Phase3", PATIENT_COUNT: "150", ...}
   │     │     After:  {PHASE: "Phase4", PATIENT_COUNT: "175", ...}
   │     │     Operation: Complete dictionary replacement
   │     │
   │     └─ [FILE UPDATE - Async Background]
   │        └─ Task.Run(WriteToFileAsync)
   │           ├─ Acquire file lock
   │           ├─ Serialize COMPLETE new state:
   │           │  {
   │           │    "metadata": {
   │           │      "userId": "7d3e7eac-...",
   │           │      "lastUpdated": "2026-06-18T15:10:00Z"  ← New
   │           │    },
   │           │    "variables": {
   │           │      "SESSIONID": "17e17538-...",
   │           │      "PROJECT": "ClinicalTrial2024",
   │           │      "PHASE": "Phase4",          ← Updated
   │           │      "PATIENT_COUNT": "175",     ← Updated
   │           │      "SITE": "Hospital_A"
   │           │    }
   │           │  }
   │           ├─ Write to temp: variables.{guid}.tmp
   │           ├─ Atomic rename: OVERWRITES variables.json
   │           └─ Release file lock
   │
   └─ Notify UI: Job complete

┌─────────────────────────────────────────────────────────────────┐
│ UPDATE MECHANISM:                                               │
│                                                                  │
│ SetAsync() always REPLACES the entire dictionary:               │
│  - Not a merge operation                                        │
│  - Complete snapshot of current SAS variable state              │
│  - File is atomically overwritten (temp file + rename)          │
│                                                                  │
│ This ensures:                                                   │
│  ✅ Updates are captured                                         │
│  ✅ Deletions are captured (if var not in new snapshot)         │
│  ✅ File always represents actual SAS state                      │
│  ✅ No partial updates or corruption                             │
└─────────────────────────────────────────────────────────────────┘
```

### Steps

1. **In the same session**, submit:
   ```sas
   /* Update existing variables */
   %let patient_count = 175;
   %let phase = Phase4;
   
   %put NOTE: Updated patient count: &patient_count;
   %put NOTE: Updated phase: &phase;
   %put NOTE: Project still: &project;
   ```

### Expected Results

#### In the Job Log (UI)
```
NOTE: Updated patient count: 175
NOTE: Updated phase: Phase4
NOTE: Project still: ClinicalTrial2024
```

#### Updated File Content
```json
{
  "metadata": {
    "userId": "7d3e7eac-c362-4f22-90b4-cd8b307d6926",
    "lastUpdated": "2026-06-18T15:10:00Z"
  },
  "variables": {
    "SESSIONID": "17e17538-6a45-4e3b-bca6-29a1514a8565",
    "PROJECT": "ClinicalTrial2024",
    "PHASE": "Phase4",
    "PATIENT_COUNT": "175",
    "SITE": "Hospital_A"
  }
}
```

✅ **Pass Criteria:**
- `PATIENT_COUNT` changed from 150 → 175
- `PHASE` changed from Phase3 → Phase4
- Other variables unchanged

---

## Test 4: Persistence After Application Restart

### Goal
Verify that variables are loaded from the file after the application restarts (true persistence).

### How Storage and Retrieval Works

```
┌─────────────────────────────────────────────────────────────────┐
│ APPLICATION RESTART - In-Memory Cache Lost!                     │
└─────────────────────────────────────────────────────────────────┘

Application stops:
  ├─ All in-memory data cleared: _store = {}
  ├─ Cache is gone: _loadedFromDisk = {}
  └─ File remains on disk: variables.json still exists ✅

Application starts:
  ├─ MacroVarStore constructor runs
  ├─ _store = {} (empty ConcurrentDictionary)
  ├─ _loadedFromDisk = {} (empty)
  └─ Ready to serve requests

┌─────────────────────────────────────────────────────────────────┐
│ REQUEST 4: First Job After Restart (Same Session)               │
└─────────────────────────────────────────────────────────────────┘

1. User clicks "Submit" → POST /api/session-job/submit
   ├─ SessionJobOrchestrator.SubmitAsync() called
   │
   ├─ [RETRIEVAL - Cache Miss, Lazy Load from Disk]
   │  └─ MacroVarStore.GetAsync(sessionId)
   │     ├─ Check in-memory cache: EMPTY (post-restart)
   │     ├─ Check _loadedFromDisk flag: FALSE (never loaded)
   │     │
   │     ├─ [LAZY LOAD FROM DISK]
   │     │  └─ LoadFromFileAsync(sessionId)
   │     │     ├─ Resolve userId from cache: FOUND
   │     │     │  (RegisterSession was called before GetAsync)
   │     │     │
   │     │     ├─ Construct file path:
   │     │     │  {StudyFolder}/sessions/{userId}/{sessionId}/variables.json
   │     │     │
   │     │     ├─ Check if file exists: YES! ✅
   │     │     │  (File survived restart)
   │     │     │
   │     │     ├─ Read file content:
   │     │     │  {
   │     │     │    "metadata": {
   │     │     │      "userId": "7d3e7eac-...",
   │     │     │      "lastUpdated": "2026-06-18T15:10:00Z"
   │     │     │    },
   │     │     │    "variables": {
   │     │     │      "SESSIONID": "17e17538-...",
   │     │     │      "PROJECT": "ClinicalTrial2024",
   │     │     │      "PHASE": "Phase4",
   │     │     │      "PATIENT_COUNT": "175",
   │     │     │      "SITE": "Hospital_A"
   │     │     │    }
   │     │     │  }
   │     │     │
   │     │     ├─ Deserialize JSON to MacroVarFile
   │     │     ├─ Validate structure (metadata, variables)
   │     │     ├─ Extract variables dictionary: 5 variables
   │     │     ├─ Cache userId from metadata
   │     │     └─ Return: Dictionary with 5 variables
   │     │
   │     ├─ [POPULATE CACHE]
   │     │  └─ _store[sessionId] = loaded variables
   │     │     Cache now contains: 5 variables (from disk)
   │     │
   │     ├─ [MARK AS LOADED]
   │     │  └─ _loadedFromDisk[sessionId] = true
   │     │     Prevents re-reading file on next GetAsync
   │     │
   │     └─ Return: Dictionary with 5 variables
   │        (These are the values from BEFORE restart!)
   │
   ├─ Build preamble WITH all 5 loaded variables
   │  LIBNAME SESSLIB "...";
   │  %let SESSIONID = "17e17538-...";     ← From file
   │  %let PROJECT = ClinicalTrial2024;    ← From file
   │  %let PHASE = Phase4;                 ← From file (updated value)
   │  %let PATIENT_COUNT = 175;            ← From file (updated value)
   │  %let SITE = Hospital_A;              ← From file
   │
   ├─ Append user code:
   │  %put NOTE: Variables after restart!
   │  %put _user_;
   │
   ├─ Submit to Altair SLC
   └─ Return jobId to UI

2. Job executes on Altair SLC
   ├─ Preamble sets ALL 5 variables (loaded from file)
   └─ User can access: &project, &phase, &patient_count, &site
      All values are PRESERVED from before restart! ✅

┌─────────────────────────────────────────────────────────────────┐
│ PERSISTENCE FLOW ACROSS RESTART:                               │
│                                                                  │
│ Before Restart:                                                 │
│   Request 3 → SetAsync()                                        │
│               ├─ Cache: 5 vars                                   │
│               └─ File: 5 vars  ← WRITTEN TO DISK                │
│                                                                  │
│ [APPLICATION RESTART]                                           │
│   ├─ Cache cleared (RAM lost)                                   │
│   └─ File persists (disk survives)                              │
│                                                                  │
│ After Restart:                                                  │
│   Request 4 → GetAsync()                                        │
│               ├─ Cache miss (empty)                             │
│               ├─ LoadFromFileAsync() ← READ FROM DISK           │
│               ├─ Populate cache with file content               │
│               └─ Return: 5 vars (restored!)                     │
│                                                                  │
│ KEY: File acts as persistent storage layer                      │
│      Cache acts as performance layer (after load)               │
└─────────────────────────────────────────────────────────────────┘
```

### Steps

1. **Note the current variables** from `variables.json`

2. **Stop the application** (Ctrl+C in terminal)

3. **Restart the application:**
   ```bash
   dotnet run
   ```

4. **In the same session** (same browser, don't close tab), submit:
   ```sas
   %put NOTE: === After Restart ===;
   %put NOTE: Project: &project;
   %put NOTE: Phase: &phase;
   %put NOTE: Patient count: &patient_count;
   %put NOTE: Site: &site;
   ```

### Expected Results

#### In the Application Log File (On First Access After Restart)
```
Variables file does not exist for session {sessionId} at path {FilePath}. This is expected for new sessions.

OR

Successfully loaded 5 variables for session {sessionId} (user {userId}) from {FilePath}
```

Then on job submission:
```
Job {jobId}: Parsed 5 macro variables from X log lines
```

#### In the Job Log (UI)
```
NOTE: === After Restart ===
NOTE: Project: ClinicalTrial2024
NOTE: Phase: Phase4
NOTE: Patient count: 175
NOTE: Site: Hospital_A
```

✅ **Pass Criteria:**
- All variables from before restart are still available
- Values are correct (Phase4, 175, etc.)

---

## Test 5: Multiple Sessions (Isolation)

### Goal
Verify that different sessions have isolated variable storage.

### How Storage and Retrieval Works

```
┌─────────────────────────────────────────────────────────────────┐
│ MULTI-SESSION ARCHITECTURE                                      │
└─────────────────────────────────────────────────────────────────┘

Storage Structure:
  MacroVarStore (singleton)
    ├─ _store: ConcurrentDictionary<sessionId, variables>
    │  ├─ ["session-A"] = {PROJECT: "ClinicalTrial2024", ...}
    │  └─ ["session-B"] = {PROJECT: "DifferentStudy", ...}
    │
    └─ File System:
       {StudyFolder}/sessions/
         ├─ {userId}/
         │  ├─ session-A/
         │  │  └─ variables.json  (PROJECT: ClinicalTrial2024)
         │  └─ session-B/
         │     └─ variables.json  (PROJECT: DifferentStudy)

Key: Each sessionId has COMPLETELY separate storage!

┌─────────────────────────────────────────────────────────────────┐
│ REQUEST 5A: New Browser Tab (Session B)                         │
└─────────────────────────────────────────────────────────────────┘

1. User opens new tab/incognito → New session created
   ├─ Browser gets new sessionId: "session-B"
   ├─ Different from original: "session-A"
   └─ Separate session directory will be used

2. User submits code in Session B:
   %let project = DifferentStudy;
   %let phase = Phase1;
   
   ├─ [RETRIEVAL - Cache Miss for Session B]
   │  └─ MacroVarStore.GetAsync("session-B")
   │     ├─ Check cache["session-B"]: EMPTY (new session)
   │     ├─ Try load from file: NO FILE (new session)
   │     └─ Return: {} (empty)
   │
   ├─ Build preamble: NO variables (first submission)
   ├─ Submit to Altair with new variables
   │
   └─ [STORAGE - Session B]
      └─ SetAsync("session-B", variables)
         ├─ Cache["session-B"] = {PROJECT: "DifferentStudy", PHASE: "Phase1"}
         │  Separate entry from Session A!
         │
         └─ File: {StudyFolder}/sessions/{userId}/session-B/variables.json
            {
              "variables": {
                "SESSIONID": "session-B",
                "PROJECT": "DifferentStudy",
                "PHASE": "Phase1"
              }
            }

┌─────────────────────────────────────────────────────────────────┐
│ REQUEST 5B: Original Tab (Session A) - Concurrent!              │
└─────────────────────────────────────────────────────────────────┘

While Session B is running, submit in original tab:

   %put NOTE: Original session project: &project;
   
   ├─ [RETRIEVAL - Cache Hit for Session A]
   │  └─ MacroVarStore.GetAsync("session-A")
   │     ├─ Check cache["session-A"]: FOUND! ✅
   │     │  {PROJECT: "ClinicalTrial2024", PHASE: "Phase4", ...}
   │     │
   │     └─ Return: Session A's variables
   │        (Completely separate from Session B!)
   │
   ├─ Build preamble with Session A variables:
   │  %let PROJECT = ClinicalTrial2024;  ← Session A value
   │  %let PHASE = Phase4;               ← Session A value
   │
   └─ Submit to Altair
      Output: "Project: ClinicalTrial2024"  ← NOT affected by Session B!

┌─────────────────────────────────────────────────────────────────┐
│ ISOLATION MECHANISM:                                            │
│                                                                  │
│ 1. Cache Isolation:                                             │
│    _store = {                                                   │
│      "session-A": {PROJECT: "ClinicalTrial2024", ...},          │
│      "session-B": {PROJECT: "DifferentStudy", ...}              │
│    }                                                            │
│    ↓                                                            │
│    Different keys = No interference!                            │
│                                                                  │
│ 2. File Isolation:                                              │
│    {StudyFolder}/sessions/{userId}/                             │
│      ├─ session-A/variables.json                                │
│      └─ session-B/variables.json                                │
│    ↓                                                            │
│    Different directories = No interference!                     │
│                                                                  │
│ 3. Concurrent Access:                                           │
│    ├─ File locks are per-sessionId (SemaphoreSlim per session)  │
│    ├─ ConcurrentDictionary handles parallel reads/writes        │
│    └─ Sessions can run simultaneously without blocking          │
│                                                                  │
│ Result: Complete isolation between sessions! ✅                  │
└─────────────────────────────────────────────────────────────────┘
```

### Steps

1. **Note your current sessionId** from the URL or logs

2. **Open a new browser tab** (or incognito window)

3. **Create a new session** (navigate to the app, should create new session)

4. **In the NEW session**, submit:
   ```sas
   %let project = DifferentStudy;
   %let phase = Phase1;
   
   %put NOTE: New session project: &project;
   %put NOTE: New session phase: &phase;
   ```

5. **In the ORIGINAL session tab**, submit:
   ```sas
   %put NOTE: Original session project: &project;
   %put NOTE: Original session phase: &phase;
   ```

### Expected Results

#### In NEW Session
```
NOTE: New session project: DifferentStudy
NOTE: New session phase: Phase1
```

#### In ORIGINAL Session
```
NOTE: Original session project: ClinicalTrial2024
NOTE: Original session phase: Phase4
```

✅ **Pass Criteria:**
- Each session has different variable values
- Two separate `variables.json` files exist (different sessionId paths)

---

## Test 6: Complex Data Types and Special Characters

### Goal
Verify that various data types and special characters are handled correctly.

### Steps

1. **Submit this code:**
   ```sas
   %let empty_var = ;
   %let numeric = 123.456;
   %let negative = -999;
   %let text_with_spaces = Hello World Test;
   %let path = /sas/data/clinical/trial;
   %let special = Value_with-dash.dot;
   %let long_text = This is a very long text value that contains multiple words and should be stored as a single string;
   
   %put NOTE: Empty: [&empty_var];
   %put NOTE: Numeric: &numeric;
   %put NOTE: Negative: &negative;
   %put NOTE: Text: &text_with_spaces;
   %put NOTE: Path: &path;
   %put NOTE: Special: &special;
   %put NOTE: Long: &long_text;
   ```

### Expected Results

#### In the Job Log (UI)
```
NOTE: Empty: []
NOTE: Numeric: 123.456
NOTE: Negative: -999
NOTE: Text: Hello World Test
NOTE: Path: /sas/data/clinical/trial
NOTE: Special: Value_with-dash.dot
NOTE: Long: This is a very long text value that contains multiple words and should be stored as a single string
```

#### In variables.json
All values should be preserved exactly as defined, including spaces and special characters.

✅ **Pass Criteria:**
- Empty variable is stored
- Spaces in values are preserved
- Special characters are preserved
- Long text is not truncated

---

## Test 7: High Volume Variables

### Goal
Verify the system can handle many variables.

### Steps

1. **Submit this code:**
   ```sas
   %let var01 = value01;
   %let var02 = value02;
   %let var03 = value03;
   %let var04 = value04;
   %let var05 = value05;
   %let var06 = value06;
   %let var07 = value07;
   %let var08 = value08;
   %let var09 = value09;
   %let var10 = value10;
   %let var11 = value11;
   %let var12 = value12;
   %let var13 = value13;
   %let var14 = value14;
   %let var15 = value15;
   %let var16 = value16;
   %let var17 = value17;
   %let var18 = value18;
   %let var19 = value19;
   %let var20 = value20;
   
   %put NOTE: Defined 20 variables;
   ```

### Expected Results

#### In the Application Log
```
Job {jobId}: Parsed 21 macro variables from X log lines
Successfully wrote 21 macro variables for session {sessionId} (user {userId}) to {FilePath}
```
(20 user vars + SESSIONID)

✅ **Pass Criteria:**
- All 20 variables are parsed
- File contains all 20 variables
- No performance issues

---

## Test 8: Concurrent Submissions (Stress Test)

### Goal
Verify that rapid successive submissions don't corrupt the file.

### How Storage and Retrieval Works

```
┌─────────────────────────────────────────────────────────────────┐
│ CONCURRENT WRITES - Race Condition Handling                     │
└─────────────────────────────────────────────────────────────────┘

Scenario: User submits 4 jobs rapidly (within seconds)

Timeline:
  T0: Submit Job 1 (%let submission = first;)
  T1: Submit Job 2 (%let submission = second;)
  T2: Submit Job 3 (%let submission = third;)
  T3: Submit Job 4 (%let submission = fourth;)

┌─────────────────────────────────────────────────────────────────┐
│ CONCURRENT EXECUTION - Multiple Requests in Flight              │
└─────────────────────────────────────────────────────────────────┘

Job 1 Background Task:
  T4: Parse logs → SUBMISSION = first
  T5: SetAsync("session", {SUBMISSION: "first"})
      ├─ Cache["session"] = {SUBMISSION: "first"}  ← FAST
      └─ Task.Run(WriteToFileAsync)  ← BACKGROUND
         [Waiting for Task.Run to schedule...]

Job 2 Background Task:
  T6: Parse logs → SUBMISSION = second
  T7: SetAsync("session", {SUBMISSION: "second"})
      ├─ Cache["session"] = {SUBMISSION: "second"}  ← OVERWRITES!
      └─ Task.Run(WriteToFileAsync)  ← BACKGROUND
         [Both write tasks now queued...]

Job 3 Background Task:
  T8: Parse logs → SUBMISSION = third
  T9: SetAsync("session", {SUBMISSION: "third"})
      ├─ Cache["session"] = {SUBMISSION: "third"}  ← OVERWRITES AGAIN!
      └─ Task.Run(WriteToFileAsync)  ← BACKGROUND

Job 4 Background Task:
  T10: Parse logs → SUBMISSION = fourth
  T11: SetAsync("session", {SUBMISSION: "fourth"})
       ├─ Cache["session"] = {SUBMISSION: "fourth"}  ← FINAL OVERWRITE
       └─ Task.Run(WriteToFileAsync)  ← BACKGROUND

[Now 4 background tasks try to write to same file...]

┌─────────────────────────────────────────────────────────────────┐
│ FILE WRITE SERIALIZATION - Per-Session Locking                  │
└─────────────────────────────────────────────────────────────────┘

MacroVarStore uses per-session SemaphoreSlim for file locking:

_fileLocks = ConcurrentDictionary<sessionId, SemaphoreSlim>

Write Task 1 (SUBMISSION = first):
  T12: GetFileLock("session") → Returns SemaphoreSlim
  T13: await fileLock.WaitAsync()  ← ACQUIRES LOCK ✅
  T14: Write to temp file: variables.{guid1}.tmp
  T15: File.Move(temp, "variables.json", overwrite: true)
       → variables.json now contains: {SUBMISSION: "first"}
  T16: fileLock.Release()  ← RELEASES LOCK

Write Task 2 (SUBMISSION = second):
  T13: await fileLock.WaitAsync()  ← BLOCKED (Task 1 has lock)
  [Waiting...]
  T16: fileLock.WaitAsync() returns  ← ACQUIRES LOCK ✅
  T17: Write to temp file: variables.{guid2}.tmp
  T18: File.Move(temp, "variables.json", overwrite: true)
       → variables.json now contains: {SUBMISSION: "second"}
       → OVERWRITES "first" ✅
  T19: fileLock.Release()

Write Task 3 (SUBMISSION = third):
  [Waited for Task 2]
  T19: Acquires lock
  T20: Write to temp: variables.{guid3}.tmp
  T21: File.Move → variables.json = {SUBMISSION: "third"}
       → OVERWRITES "second" ✅
  T22: Release lock

Write Task 4 (SUBMISSION = fourth):
  [Waited for Task 3]
  T22: Acquires lock
  T23: Write to temp: variables.{guid4}.tmp
  T24: File.Move → variables.json = {SUBMISSION: "fourth"}
       → OVERWRITES "third" ✅
  T25: Release lock

┌─────────────────────────────────────────────────────────────────┐
│ FINAL STATE:                                                    │
│                                                                  │
│ Cache:  {SUBMISSION: "fourth"}  ← Last SetAsync wins            │
│ File:   {SUBMISSION: "fourth"}  ← Last write wins               │
│                                                                  │
│ Key Protections:                                                │
│  1. SemaphoreSlim ensures ONE writer at a time per session      │
│  2. Temp file + atomic rename prevents partial writes           │
│  3. No file corruption even with 4 concurrent attempts          │
│  4. Last write wins (expected behavior)                         │
│                                                                  │
│ What if writes happen out of order?                             │
│   Example: Task 4 writes before Task 2 completes                │
│   Result: Task 2 would overwrite Task 4's write                 │
│   Behavior: Last to release lock wins (non-deterministic)       │
│   File integrity: PRESERVED (no corruption) ✅                   │
└─────────────────────────────────────────────────────────────────┘
```

### Steps

1. **Submit this code:**
   ```sas
   %let submission = first;
   ```

2. **Immediately submit again** (before first job completes):
   ```sas
   %let submission = second;
   ```

3. **Submit a third time:**
   ```sas
   %let submission = third;
   ```

4. **Submit a fourth time:**
   ```sas
   %let submission = fourth;
   ```

5. **Wait for all jobs to complete**

### Expected Results

#### In the Application Log
```
Successfully wrote X macro variables for session {sessionId} (user {userId}) to {FilePath}
Successfully wrote X macro variables for session {sessionId} (user {userId}) to {FilePath}
Successfully wrote X macro variables for session {sessionId} (user {userId}) to {FilePath}
Successfully wrote X macro variables for session {sessionId} (user {userId}) to {FilePath}
```

#### Final File Content
Should be valid JSON (not corrupted) with the last value:
```json
{
  "variables": {
    "SUBMISSION": "fourth",
    ...
  }
}
```

✅ **Pass Criteria:**
- All writes succeed
- File is valid JSON (no corruption)
- Final value is from the last submission (last-write-wins)

---

## Test 9: Integration with Datasets (End-to-End)

### Goal
Verify that macro variables work alongside dataset persistence.

### Steps

1. **Submit this code:**
   ```sas
   %let dataset_name = patients;
   %let record_count = 5;
   
   /* Create a dataset */
   data SESSLIB.&dataset_name;
       input id name $ age;
       datalines;
   1 John 45
   2 Mary 38
   3 Bob 52
   4 Alice 41
   5 Charlie 49
   ;
   run;
   
   %put NOTE: Created dataset: &dataset_name with &record_count records;
   ```

2. **In a new submission**, use the persisted variables:
   ```sas
   %put NOTE: Reading dataset: &dataset_name;
   %put NOTE: Expected records: &record_count;
   
   proc print data=SESSLIB.&dataset_name;
   run;
   ```

### Expected Results

#### First Submission
```
NOTE: Created dataset: patients with 5 records
```

Dataset file created: `patients.sas7bdat` in session folder

#### Second Submission
```
NOTE: Reading dataset: patients
NOTE: Expected records: 5
```

Then prints the dataset with 5 records.

✅ **Pass Criteria:**
- Macro variables persist
- Dataset persists
- Both work together correctly

---

## Test 10: Error Handling and Recovery

### Goal
Verify graceful handling of errors and edge cases.

### Steps

1. **Submit code with a SAS error:**
   ```sas
   %let good_var = before_error;
   
   /* This will cause an error */
   data error;
       set nonexistent_dataset;
   run;
   
   %let bad_var = after_error;
   ```

2. **Check what variables were captured**

3. **Submit valid code again:**
   ```sas
   %put NOTE: Good var: &good_var;
   %put NOTE: Bad var: &bad_var;
   ```

### Expected Results

#### After Error Submission
Variables defined BEFORE the error should be captured.

#### In Next Submission
Available variables depend on when the error occurred.

✅ **Pass Criteria:**
- System doesn't crash
- Variables defined before error are available
- Can continue with new submissions

---

## Summary Checklist

After completing all tests, verify:

- [ ] **Test 1:** Variables captured and saved on first submission
- [ ] **Test 2:** Variables available in subsequent submissions
- [ ] **Test 3:** Variable updates are persisted
- [ ] **Test 4:** Variables persist after application restart
- [ ] **Test 5:** Sessions are isolated (separate variable storage)
- [ ] **Test 6:** Special characters and data types handled correctly
- [ ] **Test 7:** Many variables handled efficiently
- [ ] **Test 8:** Concurrent submissions don't corrupt files
- [ ] **Test 9:** Integration with datasets works
- [ ] **Test 10:** Error handling is graceful

---

## Monitoring and Debugging

### Key Log File Locations

**Application Logs:**
```
SasJobRunner\logs\sasjobrunner-{date}.log
```

**Variables Files:**
```
{StudyFolder}/sessions/{userId}/{sessionId}/variables.json
```

Example:
```
/sas/studies/development/sessions/7d3e7eac-c362-4f22-90b4-cd8b307d6926/17e17538-6a45-4e3b-bca6-29a1514a8565/variables.json
```

### What to Look For in Logs

**Success Pattern:**
```
Job {jobId}: Parsed X macro variables from Y log lines
SetAsync called for session {sessionId} with X variables
WriteToFileAsync called for session {sessionId}, user {userId} with X variables
Successfully wrote X macro variables for session {sessionId} (user {userId}) to {FilePath}
```

**Failure Patterns:**

1. **Parsing Failed:**
   ```
   Job {jobId}: Parsed 0 macro variables from Y log lines
   ```

2. **UserId Not Resolved:**
   ```
   Skipping file write for session {sessionId}: userId could not be resolved
   ```

3. **File Write Failed:**
   ```
   I/O error when writing variables file for session {sessionId}
   ```

---

## Expected Performance

- **Variable Parsing:** < 100ms
- **File Write:** < 500ms (async, doesn't block)
- **File Read (on restart):** < 100ms
- **20 Variables:** No noticeable delay
- **100 Variables:** < 1 second total

---

## Troubleshooting

### Variables Not Persisting

1. Check log file for "Parsed 0 macro variables"
2. Verify `variables.json` file exists
3. Check StudyFolder configuration
4. Verify userId is resolved

### Wrong Values After Restart

1. Check file modification timestamp
2. Verify correct session is being used
3. Check for multiple session folders

### File Corruption

1. Check for concurrent write errors in logs
2. Look for partial JSON or invalid format
3. Delete corrupted file and retry

---

## Success Criteria

The feature is working correctly if:

✅ Variables are captured from job logs  
✅ Variables are saved to `variables.json` files  
✅ Variables are loaded in subsequent submissions (same session)  
✅ Variables persist after application restart  
✅ Different sessions have isolated variable storage  
✅ File writes are atomic (no corruption)  
✅ Performance is acceptable  

Happy Testing! 🚀
