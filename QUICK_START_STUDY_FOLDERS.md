# Quick Start: Study Folder Configuration

## What Changed?

Session storage paths are now configurable via `appsettings.json` instead of being hardcoded.

## How to Configure

### Step 1: Edit appsettings.json

Add this section to your `appsettings.json` or `appsettings.Development.json`:

```json
{
  "SessionStorage": {
    "StudyFolder": "/sas/studies/your-study-name"
  }
}
```

### Step 2: That's it!

The application will now create session folders under your specified study folder.

## Path Examples

| Configuration | User ID | Session ID | Final Path |
|--------------|---------|------------|------------|
| `/sas/studies/clinical-trial` | `user@example.com` | `abc123` | `/sas/studies/clinical-trial/sessions/user@example.com/abc123/` |
| `/data/prod-study` | `researcher` | `xyz789` | `/data/prod-study/sessions/researcher/xyz789/` |
| `/sas` | `admin` | `test01` | `/sas/sessions/admin/test01/` |

## Common Configurations

### Development Environment

```json
{
  "SessionStorage": {
    "StudyFolder": "/sas/studies/dev-study"
  }
}
```

### Production Environment

```json
{
  "SessionStorage": {
    "StudyFolder": "/sas/studies/prod-study"
  }
}
```

### Keep Old Behavior

To maintain the previous hardcoded path (`/sas/sessions/...`):

```json
{
  "SessionStorage": {
    "StudyFolder": "/sas"
  }
}
```

## What Gets Generated

When you run SAS code, the application automatically adds this preamble:

```sas
LIBNAME SESSLIB "{YourStudyFolder}/sessions/{userId}/{sessionId}/";
%let SESSIONID={sessionId};
```

## Testing It

### 1. Update Configuration

Edit `appsettings.Development.json`:

```json
{
  "SessionStorage": {
    "StudyFolder": "/sas/studies/test-study"
  }
}
```

### 2. Run the Application

```powershell
cd SasJobRunner
dotnet run
```

### 3. Submit Test Code

```sas
/* This will create dataset in: */
/* /sas/studies/test-study/sessions/{userId}/{sessionId}/mydata.sas7bdat */

data SESSLIB.mydata;
    x = 1;
    y = 2;
run;

/* Verify the library path */
proc sql;
    select libname, path 
    from dictionary.libnames 
    where libname='SESSLIB';
quit;
```

### 4. Check the Output

The log will show:
```
LIBNAME SESSLIB "/sas/studies/test-study/sessions/{userId}/{sessionId}/";
```

## Error: Missing Configuration

If you see this error:

```
InvalidOperationException: SessionStorage:StudyFolder configuration is required.
```

**Fix:** Add the `SessionStorage:StudyFolder` setting to your appsettings file.

## Multiple Studies

You can run different instances with different study folders:

**Instance 1 (Study A):**
```json
{ "SessionStorage": { "StudyFolder": "/sas/studies/study-a" } }
```

**Instance 2 (Study B):**
```json
{ "SessionStorage": { "StudyFolder": "/sas/studies/study-b" } }
```

Sessions in different studies are completely isolated!

## Session Persistence

✅ **Same Session = Same Data**
- Submit code to create a dataset
- Submit more code in the **same session**
- Dataset is still available

❌ **Different Session = No Data**
- Logout or clear cookies = new session
- New session has its own folder
- Previous datasets are NOT available

## File Organization on Server

```
/sas/studies/your-study/
└── sessions/
    └── user@example.com/
        ├── session-1/
        │   ├── dataset1.sas7bdat
        │   └── dataset2.sas7bdat
        └── session-2/
            └── dataset3.sas7bdat
```

## Pro Tips

1. **Use Descriptive Names**: `/sas/studies/cardiovascular-2024` instead of `/sas/studies/study1`

2. **Environment-Specific**: Different folders for dev/test/prod keeps data isolated

3. **Check Permissions**: Ensure the SLC Hub server can write to your study folder

4. **Verify Path**: Use `proc sql; select * from dictionary.libnames; quit;` to see actual paths

5. **Session Variables**: Use `%put _user_;` to see all your macro variables from previous runs

## Ready to Use!

✅ Configuration added to both appsettings files
✅ PreambleBuilder updated to use configuration
✅ All tests passing
✅ No breaking changes

Just update the `StudyFolder` value to match your environment and start coding!
