# Windows Execution Profile - Path Configuration Guide

## For Windows SAS Execution Profiles

If you're using a **Windows execution profile** on Altair SLC Hub, your network paths should follow Windows conventions.

## Your Network Path

```
\\filer\home\study1\datasets\
```

## Configuration Options

### ✅ Option 1: Forward Slashes (Recommended)

**SAS on Windows accepts forward slashes**, and this is easier in JSON configuration:

```json
{
  "SessionStorage": {
    "StudyFolder": "//filer/home/study1/datasets"
  }
}
```

**Generated SAS Code:**
```sas
LIBNAME SESSLIB "//filer/home/study1/datasets/sessions/user@example.com/abc123/";
```

**Advantages:**
- ✅ No escaping needed
- ✅ Cleaner JSON syntax
- ✅ Works perfectly with SAS on Windows
- ✅ Less error-prone

### Option 2: Escaped Backslashes

Use your original UNC path with **doubled backslashes**:

```json
{
  "SessionStorage": {
    "StudyFolder": "\\\\filer\\home\\study1\\datasets"
  }
}
```

**Generated SAS Code:**
```sas
LIBNAME SESSLIB "\\filer\home\study1\datasets\sessions\user@example.com\abc123\";
```

**Advantages:**
- ✅ True Windows UNC format
- ✅ Familiar Windows convention

**Disadvantages:**
- ⚠️ Must escape backslashes (easy to forget)
- ⚠️ More verbose

### Option 3: Mapped Drive Letter

If your network share is mapped to a drive letter on the Windows server (e.g., `S:`):

```json
{
  "SessionStorage": {
    "StudyFolder": "S:/study1/datasets"
  }
}
```

**Generated SAS Code:**
```sas
LIBNAME SESSLIB "S:/study1/datasets/sessions/user@example.com/abc123/";
```

## Complete Configuration Example

### appsettings.Development.json

```json
{
  "Logging": {
    "LogLevel": {
      "Default": "Debug",
      "Microsoft.AspNetCore": "Information"
    }
  },
  "SlcHub": {
    "BaseUrl": "http://your-slc-hub:8080",
    "Namespace": "default",
    "ExecutionProfile": "windows-profile",
    "UserId": "your-user-id",
    "ServiceAccount": {
      "Username": "domain\\serviceaccount",
      "Password": "your-password"
    }
  },
  "SessionStorage": {
    "StudyFolder": "//filer/home/study1/datasets"
  }
}
```

## Path Construction

With the above configuration:

| Component | Value |
|-----------|-------|
| Study Folder | `//filer/home/study1/datasets` |
| User ID | `researcher@example.com` |
| Session ID | `abc123` |
| **Final Path** | `//filer/home/study1/datasets/sessions/researcher@example.com/abc123/` |

## Testing Your Configuration

### Step 1: Test Direct Access

Submit this SAS code to verify the base path works:

```sas
/* Test if you can access the network share */
libname test "//filer/home/study1/datasets";

/* Create a test dataset */
data test.temptest;
    x = 1;
    y = 2;
run;

/* Verify it exists */
proc print data=test.temptest;
run;

/* Clean up */
proc datasets library=test nolist;
    delete temptest;
quit;
```

### Step 2: Submit Application Code

After Step 1 works, submit code through your application:

```sas
/* The application will add the preamble automatically */
/* LIBNAME SESSLIB "//filer/home/study1/datasets/sessions/{userId}/{sessionId}/"; */

/* Create a dataset in your session folder */
data SESSLIB.mydata;
    input name $ value;
    datalines;
John 100
Mary 200
Bob 150
;
run;

/* Verify the path */
proc sql;
    select libname, path 
    from dictionary.libnames 
    where libname='SESSLIB';
quit;
```

### Step 3: Verify Persistence

Submit a second job in the same session:

```sas
/* The dataset should still be there */
proc print data=SESSLIB.mydata;
run;

/* Add more data */
data SESSLIB.moredata;
    set SESSLIB.mydata;
    newvalue = value * 2;
run;
```

## Common Issues and Solutions

### Issue 1: Access Denied

**Error:**
```
ERROR: Physical file does not exist, //filer/home/study1/datasets/...
ERROR: Error in the LIBNAME statement.
```

**Solutions:**
- Verify the Windows service account has read/write access to `\\filer\home\study1\datasets`
- Check network connectivity from the SLC Hub Windows server
- Ensure the path exists on the file server

### Issue 2: JSON Parsing Error

**Error:**
```
Unexpected character encountered while parsing value: \
```

**Solution:**
If using backslashes, make sure they're doubled:
```json
"StudyFolder": "\\\\filer\\home\\study1\\datasets"
```

Or use forward slashes instead:
```json
"StudyFolder": "//filer/home/study1/datasets"
```

### Issue 3: Invalid Path Characters

**Error:**
```
ERROR: The physical name is not valid.
```

**Solution:**
- Avoid special characters in folder names
- Use forward slashes or properly escaped backslashes
- Ensure no trailing spaces in JSON configuration

## Permissions Requirements

The **SLC Hub service account** needs:

- ✅ **Read** access to the study folder
- ✅ **Write** access to create session subfolders
- ✅ **Modify** access to create/update datasets
- ✅ **Network access** from the SLC Hub Windows server to the file server

Check with your IT administrator to ensure these permissions are granted.

## File Structure on Network Share

After running some sessions, your network share will look like:

```
\\filer\home\study1\datasets\
└── sessions\
    ├── researcher@example.com\
    │   ├── abc123\
    │   │   ├── mydata.sas7bdat
    │   │   ├── analysis.sas7bdat
    │   │   └── results.sas7bdat
    │   └── xyz789\
    │       └── tempdata.sas7bdat
    └── analyst@example.com\
        └── def456\
            └── report.sas7bdat
```

## Real-World Examples

### Clinical Trial Study

```json
{
  "SessionStorage": {
    "StudyFolder": "//clinical-server/studies/trial-2024"
  }
}
```

### Departmental Share

```json
{
  "SessionStorage": {
    "StudyFolder": "//dept-server/analytics/sas-work"
  }
}
```

### Local Server Path

```json
{
  "SessionStorage": {
    "StudyFolder": "D:/SASData/Studies/Production"
  }
}
```

### Mapped Drive

```json
{
  "SessionStorage": {
    "StudyFolder": "S:/Studies/Current"
  }
}
```

## Best Practices

1. **Use Forward Slashes** - They work on Windows SAS and are easier in JSON

2. **Test First** - Verify network access with a simple LIBNAME statement before configuring the application

3. **Check Permissions** - Ensure the service account can create folders and files

4. **Use Descriptive Names** - Name your study folder clearly (e.g., `study1-datasets` instead of just `data`)

5. **Document the Path** - Keep a record of which network share maps to which study

6. **Monitor Disk Space** - Session folders accumulate over time; plan cleanup strategy

## Summary

✅ **For your path** `\\filer\home\study1\datasets\`

**Use this configuration:**
```json
{
  "SessionStorage": {
    "StudyFolder": "//filer/home/study1/datasets"
  }
}
```

SAS on Windows will correctly interpret this as: `\\filer\home\study1\datasets\sessions\{userId}\{sessionId}\`

No changes to the application code needed - it already handles both Windows and Linux paths correctly!
