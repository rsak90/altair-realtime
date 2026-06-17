# Log Parsing Debug - Step 4 Issue

## Problem Identified

The process stops at **Step 4: "Parsed 0 macro variables from logs"**

This means the `%put _user_;` output is not being found or parsed correctly from the job logs.

## Enhanced Logging Added

I've added detailed diagnostic logging to help identify the issue:

### 1. LogParserService.ParseUserMacroVars
Now logs:
- When SESSIONID= line is found (start of _user_ block)
- Each variable parsed with its value
- When the _user_ block ends
- Summary: total lines, skipped lines, parsed variables

**Console output format:**
```
[LogParser] Found SESSIONID= line at line 42: SESSIONID=abc123
[LogParser] Parsed variable: MYVAR = test123
[LogParser] Parsed variable: ANOTHER = value456
[LogParser] End of _user_ block detected at line 45: NOTE: ...
[LogParser] Summary: Total lines=150, Skipped=5, Parsed=2, Result count=2
```

### 2. SessionJobOrchestrator.StreamAndFinalizeAsync
Now logs:
- Total number of log lines captured
- First 10 and last 10 log lines if no variables are parsed (Debug level)

## How %put _user_; Should Look in SAS Logs

When `%put _user_;` executes successfully, the SAS log should contain:

```
SESSIONID=abc123
MYVAR=test123
ANOTHER=value456
```

**Key points:**
1. Must start with `SESSIONID=` (this triggers the parser to start reading)
2. Each variable on its own line in format `NAME=VALUE`
3. No prefixes like NOTE:, ERROR:, WARNING: on these lines
4. Variables are uppercase by default

## Common Reasons for Parsing Failure

### Reason 1: SESSIONID Not Found
The parser looks for a line starting with `SESSIONID=` to know where to start parsing.

**Check:**
- Is `SESSIONID` macro variable being set in the preamble? ✅ (It is - line 28 in PreambleBuilder)
- Is the log being truncated before `%put _user_;` executes?
- Is the job failing before reaching `%put _user_;`?

### Reason 2: Log Format Different
SAS might be adding prefixes or formatting the output differently.

**Possible formats that would fail:**
```
NOTE: SESSIONID=abc123          ❌ (has NOTE: prefix)
  SESSIONID=abc123              ✅ (whitespace is trimmed)
MPRINT(XYZ): SESSIONID=abc123   ❌ (filtered out - has MPRINT)
```

### Reason 3: %put _user_; Not Executing
The code might be failing before `%put _user_;` is reached.

**Check:**
- Does the job status show CompletedSuccess?
- Are there errors in the log before the end?

### Reason 4: Log Not Being Captured
The log fetching might be incomplete or empty.

**Check:**
- Are log lines actually being captured? (Check the log count)
- Is the log content empty or truncated?

## What to Check Next

### 1. Rebuild and Run
```bash
cd SasJobRunner
dotnet build
dotnet run
```

### 2. Submit Test Job
```sas
%put NOTE: === START OF TEST ===;
%let myvar = test123;
%let another = value456;
%put NOTE: About to execute put user;
%put _user_;
%put NOTE: === END OF TEST ===;
```

### 3. Check Console Output

You should see in the console (not just logs):
```
[LogParser] Found SESSIONID= line at line X: SESSIONID=...
[LogParser] Parsed variable: MYVAR = test123
[LogParser] Parsed variable: ANOTHER = value456
[LogParser] Summary: Total lines=..., Skipped=..., Parsed=2, Result count=2
```

### 4. Check Application Logs

Look for:
```
Job {jobId}: Parsed 2 macro variables from {N} log lines
```

If still 0, check the debug logs showing first/last 10 lines:
```
Job {jobId}: First 10 log lines:
  NOTE: ...
  NOTE: ...
  ...
Job {jobId}: Last 10 log lines:
  NOTE: ...
  SESSIONID=abc123
  MYVAR=test123
  ...
```

## Expected vs Actual Log Format

### Expected (What Parser Needs)

```
[Other log content...]
SESSIONID=abc123
MYVAR=test123
ANOTHER=value456
[More log content...]
```

### What Might Be Happening

**Scenario A: Prefixed Lines**
```
NOTE: SESSIONID=abc123
NOTE: MYVAR=test123
NOTE: ANOTHER=value456
```
❌ Parser expects lines starting with variable name, not NOTE:

**Scenario B: Job Failed Early**
```
ERROR: Some error
[Job stops before %put _user_;]
```
❌ No _user_ output to parse

**Scenario C: Empty Log**
```
[No content]
```
❌ Log not being fetched correctly

**Scenario D: MPRINT/MLOGIC Wrapping**
```
MPRINT(ABC): SESSIONID=abc123
MLOGIC(ABC): MYVAR=test123
```
❌ These lines are filtered out by design

## Quick Verification Test

To rule out parsing logic issues, let's verify the regex works:

The parser uses: `^([A-Z_][A-Z0-9_]*)=(.*)$`

**Matches:**
- ✅ `SESSIONID=abc123`
- ✅ `MYVAR=test123`
- ✅ `_PRIVATE=value`
- ✅ `VAR123=hello world`

**Doesn't match:**
- ❌ `NOTE: SESSIONID=abc123` (starts with NOTE:)
- ❌ `  SESSIONID=abc123` (leading spaces - but TrimStart() should handle this)
- ❌ `myvar=test` (lowercase - SAS converts to uppercase)
- ❌ `123VAR=test` (starts with number)

## Next Steps

1. **Run the updated code** with enhanced logging
2. **Look at the console output** - it will show exactly what the parser is seeing
3. **Check the application logs** (Debug level) for first/last 10 lines
4. **Report back what you see:**
   - Is SESSIONID= line found?
   - What do the first/last 10 lines look like?
   - What does the console output show?

The enhanced logging will tell us exactly what's in the log and why the parser can't find the variables! 🔍
