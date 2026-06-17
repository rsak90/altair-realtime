# Debugging: Missing INVESTIGATOR Variable

## Issue
- You defined 3 variables: `study_name`, `investigator`, `site_count`
- In the log, you see all 3 as GLOBAL variables
- In variables.json, only `STUDY_NAME` and `SITE_COUNT` appear
- `INVESTIGATOR` is missing

## Possible Causes

### Cause 1: Space in Value "Dr Smith"
The value `Dr Smith` has a space. The regex should handle this, but let's verify.

**Pattern:** `^GLOBAL\s+([A-Z_][A-Z0-9_]*)\s+(.*)$`
- `(.*)` should capture everything including spaces

**Test:** Does your log show:
```
GLOBAL INVESTIGATOR Dr Smith
```
Or something else?

### Cause 2: Line Continuation
Some SAS versions split long lines. It might look like:
```
GLOBAL INVESTIGATOR Dr
Smith
```

### Cause 3: Special Characters in Variable Name
If the variable name has a space or special character, the regex won't match.

**Example that FAILS:**
```
GLOBAL INVESTIGATOR NAME Dr Smith  ← Space in variable name
```

**Variable name regex:** `[A-Z_][A-Z0-9_]*`
- Must start with letter or underscore
- Can only contain letters, numbers, underscores
- NO spaces allowed

### Cause 4: Dictionary Overwrite Bug
The parser uses `OrdinalIgnoreCase` comparison. If there's a case mismatch or duplicate, one might overwrite another.

## Enhanced Logging

I've added logging to show:
1. Lines in the block that don't match the pattern
2. All parsed variables at the end

### How to Debug

1. **Rebuild and run:**
   ```bash
   cd SasJobRunner
   dotnet build
   dotnet run
   ```

2. **Submit the test again:**
   ```sas
   %let study_name = CardiacTrial2024;
   %let investigator = Dr Smith;
   %let site_count = 15;
   ```

3. **Check console output for:**
   ```
   [LogParser] Found SESSIONID line at line X: ...
   [LogParser] Parsed variable (format 2): SESSIONID = ...
   [LogParser] Parsed variable (format 2): STUDY_NAME = CardiacTrial2024
   [LogParser] Parsed variable (format 2): INVESTIGATOR = Dr Smith  ← Look for this!
   [LogParser] Parsed variable (format 2): SITE_COUNT = 15
   [LogParser] Summary: Total lines=X, Skipped=Y, Parsed=3, Result count=3
   [LogParser] All parsed variables:
   [LogParser]   SESSIONID = ...
   [LogParser]   STUDY_NAME = CardiacTrial2024
   [LogParser]   INVESTIGATOR = Dr Smith  ← And this!
   [LogParser]   SITE_COUNT = 15
   ```

4. **If you see "In block but line doesn't match pattern":**
   ```
   [LogParser] In block but line doesn't match pattern at line X: GLOBAL INVESTIGATOR Dr Smith
   ```
   This means the regex isn't matching. Share that line with me.

5. **Check the application log file:**
   ```
   SasJobRunner\logs\sasjobrunner-{date}.log
   ```
   Search for "INVESTIGATOR" to see if it was parsed.

6. **Check the complete log dump:**
   In the log file, find:
   ```
   ==================== START LOG ====================
   ...
   GLOBAL INVESTIGATOR ...
   ...
   ==================== END LOG ====================
   ```
   Share what the INVESTIGATOR line looks like exactly.

## Quick Test

Try with a value WITHOUT spaces:
```sas
%let study_name = CardiacTrial2024;
%let investigator = DrSmith;
%let site_count = 15;
```

If this works, the issue is with how spaces are being handled.

## Alternative: Use Quotes

Try:
```sas
%let investigator = "Dr Smith";
```

Or underscore:
```sas
%let investigator = Dr_Smith;
```

## What to Share

After running with enhanced logging, share:

1. **Console output** showing the [LogParser] lines
2. **The exact line from your SAS log** showing GLOBAL INVESTIGATOR
3. **The variables.json content**
4. **Any "doesn't match pattern" messages**

This will tell us exactly why INVESTIGATOR is being skipped!
