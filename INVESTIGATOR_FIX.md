# Fix: Missing INVESTIGATOR Variable

## Root Cause Found! 🎯

Looking at your log output, the issue is clear:

```
Line 27: GLOBAL INVESTIGATOR DrSmith          ← Appeared FIRST
Line 28: GLOBAL SESSIONID 5c72c8ab-3ebe-...   ← Parser started here
Line 30: NOTE: Submitted statements took :     ← Parser ended here
```

**The Problem:**
- Parser was looking for `SESSIONID` to start the block
- `INVESTIGATOR` appeared BEFORE `SESSIONID` in the log
- Parser skipped `INVESTIGATOR` because it wasn't "in block" yet
- Parser found `SESSIONID`, started the block
- Parser immediately hit the `NOTE:` line and ended the block
- Result: Only `SESSIONID` was captured!

## Why Variables Appear in Different Order

SAS outputs variables in **alphabetical order** when you use `%put user;` or `%put _user_;`:

```
GLOBAL INVESTIGATOR DrSmith     ← "I" comes before "S"
GLOBAL SESSIONID ...            ← "S" comes after "I"
```

So if you define variables in any order, `%put _user_;` will output them alphabetically.

## The Fix

Changed the parser to start the block on **ANY** `GLOBAL` line, not just `SESSIONID`:

### Before:
```csharp
// Only started on SESSIONID
if (trimmed.StartsWith("SESSIONID=", ...) ||
    trimmed.StartsWith("GLOBAL SESSIONID", ...))
{
    inBlock = true;
}
```

### After:
```csharp
// Starts on ANY GLOBAL variable
if (trimmed.StartsWith("SESSIONID=", ...) ||
    trimmed.StartsWith("GLOBAL SESSIONID", ...) ||
    trimmed.StartsWith("GLOBAL ", ...))  ← NEW!
{
    inBlock = true;
}
```

Now the parser will:
1. Start the block when it sees `GLOBAL INVESTIGATOR`
2. Parse: `INVESTIGATOR = DrSmith` ✅
3. Continue parsing
4. Parse: `SESSIONID = 5c72c8ab-...` ✅
5. Stop at `NOTE:` line

## Testing

1. **Rebuild:**
   ```bash
   cd SasJobRunner
   dotnet build
   dotnet run
   ```

2. **Run the test again:**
   ```sas
   %let study_name = CardiacTrial2024;
   %let investigator = Dr Smith;
   %let site_count = 15;
   ```

3. **Expected console output:**
   ```
   [LogParser] Found start of user variables block at line X: GLOBAL INVESTIGATOR Dr Smith
   [LogParser] Parsed variable (format 2): INVESTIGATOR = Dr Smith
   [LogParser] Parsed variable (format 2): SESSIONID = ...
   [LogParser] Parsed variable (format 2): SITE_COUNT = 15
   [LogParser] Parsed variable (format 2): STUDY_NAME = CardiacTrial2024
   [LogParser] Summary: Total lines=X, Skipped=Y, Parsed=4, Result count=4
   [LogParser] All parsed variables:
   [LogParser]   INVESTIGATOR = Dr Smith  ← NOW INCLUDED!
   [LogParser]   SESSIONID = ...
   [LogParser]   SITE_COUNT = 15
   [LogParser]   STUDY_NAME = CardiacTrial2024
   ```

4. **Check variables.json:**
   ```json
   {
     "variables": {
       "INVESTIGATOR": "Dr Smith",  ← NOW PRESENT!
       "SESSIONID": "...",
       "SITE_COUNT": "15",
       "STUDY_NAME": "CardiacTrial2024"
     }
   }
   ```

## Why This Wasn't Caught Earlier

The original design assumed:
- `SESSIONID` would always be the first variable in the output
- This was true when `SESSIONID` was defined in the preamble first
- But SAS outputs variables **alphabetically**, not in definition order
- So variables starting with letters before "S" would appear first

## Lesson Learned

**SAS Variable Output Order:**
- `%put _user_;` outputs variables in **alphabetical order**
- Not in the order they were defined
- Not in the order they appear in the preamble

**Examples:**
```sas
%let zebra = 1;
%let apple = 2;
%let middle = 3;

%put _user_;
```

**Output:**
```
GLOBAL APPLE 2      ← A comes first
GLOBAL MIDDLE 3     ← M comes next
GLOBAL ZEBRA 1      ← Z comes last
```

## Impact on Other Variables

Any variable that comes alphabetically **before SESSIONID** would have been missed:

**Variables that would be MISSED (before fix):**
- INVESTIGATOR ← "I" < "S"
- ENROLLMENT
- BASELINE
- ANALYSIS
- Any variable starting with A-R

**Variables that would be CAPTURED (before fix):**
- SESSIONID ← "S"
- STUDY_NAME ← "S" but after SESSIONID
- TREATMENT
- ZEBRA
- Any variable starting with S-Z (after "SE...")

## Summary

✅ **Root cause:** Parser started on SESSIONID, missed earlier alphabetical variables  
✅ **Fix:** Parser now starts on ANY GLOBAL variable  
✅ **Result:** All variables captured regardless of alphabetical order  
✅ **Impact:** Fixes any variable starting with A-R (before "SESSIONID")  

The fix is complete - rebuild and test! 🎉
