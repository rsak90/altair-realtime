# Study Folder Configuration

## Overview

The SAS Job Runner now supports configurable parent directories (study folders) for organizing session data. Instead of hardcoding session paths, you can specify a study folder in the application settings.

## Configuration

### Setting the Study Folder

Add the `SessionStorage:StudyFolder` setting to your `appsettings.json` or `appsettings.Development.json`:

```json
{
  "SessionStorage": {
    "StudyFolder": "/sas/studies/my-study"
  }
}
```

### Path Construction

The application constructs session library paths as follows:

```
{StudyFolder}/sessions/{userId}/{sessionId}/
```

**Example:**
- Study Folder: `/sas/studies/clinical-trial-2024`
- User ID: `researcher@example.com`
- Session ID: `abc123`
- **Final Path:** `/sas/studies/clinical-trial-2024/sessions/researcher@example.com/abc123/`

### Generated SAS Code

With the above configuration, the preamble will generate:

```sas
LIBNAME SESSLIB "/sas/studies/clinical-trial-2024/sessions/researcher@example.com/abc123/";
%let SESSIONID=abc123;
```

## Use Cases

### Multi-Study Environment

Organize different studies with separate folders:

**Production (appsettings.json):**
```json
{
  "SessionStorage": {
    "StudyFolder": "/sas/studies/prod-study"
  }
}
```

**Development (appsettings.Development.json):**
```json
{
  "SessionStorage": {
    "StudyFolder": "/sas/studies/dev-study"
  }
}
```

**Testing:**
```json
{
  "SessionStorage": {
    "StudyFolder": "/sas/studies/test-study"
  }
}
```

### Per-Environment Isolation

- **Development:** `/sas/studies/dev`
- **Staging:** `/sas/studies/staging`
- **Production:** `/sas/studies/prod`

Each environment maintains isolated session data.

### Multi-Tenant Studies

Different research projects can have their own folders:

- **Study A:** `/sas/studies/cardiovascular-trial`
- **Study B:** `/sas/studies/oncology-trial`
- **Study C:** `/sas/studies/diabetes-trial`

## Path Validation

The application:
- **Requires** the `SessionStorage:StudyFolder` configuration (throws `InvalidOperationException` if missing)
- **Automatically trims** trailing slashes for consistent path construction
- **Preserves** leading slashes for absolute paths

## Testing Multi-Submission Scenarios

### Example: Create Dataset in One Session, Read in Another

**Configuration:**
```json
{
  "SessionStorage": {
    "StudyFolder": "/sas/studies/my-test-study"
  }
}
```

**First Submission (Session A):**
```sas
/* Creates: /sas/studies/my-test-study/sessions/{userId}/{sessionA}/employees.sas7bdat */
data SESSLIB.employees;
    input empid name $ salary;
    datalines;
101 John 50000
102 Mary 60000
;
run;
```

**Second Submission (Same Session A):**
```sas
/* Reads from same location */
proc print data=SESSLIB.employees;
run;
```

**Different Session (Session B):**
```sas
/* This will NOT see the employees dataset - different session folder */
/* Path: /sas/studies/my-test-study/sessions/{userId}/{sessionB}/ */
proc print data=SESSLIB.employees;
run;
/* ERROR: File SESSLIB.EMPLOYEES does not exist. */
```

## Migration from Hardcoded Paths

### Before (Hardcoded)

```csharp
// Old code
sb.AppendLine($"""LIBNAME SESSLIB "/sas/sessions/{userId}/{sessionId}/";""");
```

Generated: `/sas/sessions/{userId}/{sessionId}/`

### After (Configurable)

```csharp
// New code
var baseFolder = _studyFolder.TrimEnd('/');
sb.AppendLine($"""LIBNAME SESSLIB "{baseFolder}/sessions/{userId}/{sessionId}/";""");
```

Generated: `{StudyFolder}/sessions/{userId}/{sessionId}/`

### Backward Compatibility

To maintain the old behavior, set:

```json
{
  "SessionStorage": {
    "StudyFolder": "/sas"
  }
}
```

This produces: `/sas/sessions/{userId}/{sessionId}/` (same as before)

## File Structure on SLC Hub Server

```
{StudyFolder}/
└── sessions/
    ├── user1@example.com/
    │   ├── session-abc123/
    │   │   ├── employees.sas7bdat
    │   │   ├── results.sas7bdat
    │   │   └── temp_data.sas7bdat
    │   └── session-def456/
    │       └── analysis.sas7bdat
    └── user2@example.com/
        └── session-xyz789/
            └── dataset1.sas7bdat
```

## Configuration Reference

### Required Settings

| Setting | Type | Required | Description |
|---------|------|----------|-------------|
| `SessionStorage:StudyFolder` | string | Yes | Parent directory for all session folders |

### Example Configurations

**Absolute Unix Path:**
```json
{
  "SessionStorage": {
    "StudyFolder": "/data/sas/studies/trial-2024"
  }
}
```

**Relative Path (if supported by SAS):**
```json
{
  "SessionStorage": {
    "StudyFolder": "./studies/dev"
  }
}
```

**Windows Path (escaped):**
```json
{
  "SessionStorage": {
    "StudyFolder": "C:\\SASData\\Studies\\Production"
  }
}
```

## Error Handling

### Missing Configuration

If `SessionStorage:StudyFolder` is not configured, the application throws:

```
InvalidOperationException: SessionStorage:StudyFolder configuration is required.
```

**Solution:** Add the configuration to your appsettings file.

### SAS Library Error

If the path doesn't exist or SAS can't access it, you'll see in the log:

```
ERROR: Physical file does not exist, /sas/studies/...
```

**Solution:** Ensure the folder exists on the SLC Hub server and has proper permissions.

## Implementation Details

### Modified Files

1. **`appsettings.json`** - Added `SessionStorage:StudyFolder` configuration
2. **`appsettings.Development.json`** - Added development-specific study folder
3. **`PreambleBuilder.cs`** - Modified to read configuration and construct dynamic paths

### Dependency Injection

`PreambleBuilder` is registered as a scoped service and receives `IConfiguration` through constructor injection:

```csharp
builder.Services.AddScoped<PreambleBuilder>();
```

The configuration is automatically injected by the ASP.NET Core DI container.

## Summary

✅ **Flexible Study Organization** - Configure different study folders per environment
✅ **Multi-Tenant Support** - Isolate different studies/projects
✅ **Environment-Specific** - Use different folders for dev/staging/prod
✅ **Backward Compatible** - Can replicate old behavior with `/sas`
✅ **Validated on Startup** - Throws clear error if configuration is missing
