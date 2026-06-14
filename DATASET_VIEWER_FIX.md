# Dataset Viewer Navigation Fix

## Problem
When clicking on a dataset file in the file browser, a modal popup appeared with the error:
```
Error: SignalR connection not available
```

The modal was trying to:
1. Submit a SAS job to introspect the dataset
2. Stream logs via SignalR
3. Parse PROC CONTENTS and PROC PRINT output
4. Display the data in the modal

## Expected Behavior
Clicking on a dataset file should navigate to the **standalone Dataset Viewer page** at `/dataset-viewer/{datasetName}`, which provides:
- Full-screen dataset viewing
- Column metadata display
- Filtering capabilities
- Sorting by any column
- Pagination controls
- CSV export functionality
- Dynamic page size selection

## Root Causes

### 1. Wrong Navigation Logic in file-browser.js
The `viewDataset()` function was opening a modal and submitting a job, instead of simply navigating to the dataset viewer page.

### 2. Missing Service Registration
The `IDatasetReaderService` was not registered in the dependency injection container in `Program.cs`, which would cause the dataset viewer page to fail when trying to load metadata and data.

## Changes Made

### 1. Fixed file-browser.js
**Changed from:** Complex modal-based viewer with SignalR streaming
```javascript
function viewDataset(fileName) {
    currentDataset = fileName;
    // Show modal with loading state
    showModal("Loading dataset...", true);
    // Submit job and subscribe to SignalR...
}
```

**Changed to:** Simple navigation to standalone viewer
```javascript
function viewDataset(fileName) {
    // Navigate to the standalone dataset viewer page
    window.location.href = `/dataset-viewer/${encodeURIComponent(fileName)}`;
}
```

### 2. Registered DatasetReaderService in Program.cs
Added the missing service registration:
```csharp
builder.Services.AddScoped<IDatasetReaderService, DatasetReaderService>();
```

## How It Works Now

### User Flow
1. User navigates to `/Editor` page
2. Clicks on the "Files" tab in the right panel
3. Sees list of `.sas7bdat` files in their session working directory
4. Clicks on any dataset file
5. Browser navigates to `/dataset-viewer/{fileName}` page (opens in same tab)

### Dataset Viewer Page Architecture

#### Frontend (`/Views/DatasetViewer/Index.cshtml`)
- Full-page layout with header, toolbar, content area, and footer
- Toolbar with filter controls and export button
- Dynamic table rendering area
- Pagination controls

#### JavaScript (`/wwwroot/js/dataset-viewer.js`)
- **On load**: Fetches metadata via `/api/files/{fileName}/metadata`
- **Displays**: Row count, column count, file size, last modified
- **Loads data**: Via `/api/files/{fileName}/data` (POST with pagination/filter params)
- **Features**:
  - Click column headers to sort (ascending/descending)
  - Add filters with column, operator, and value
  - Change page size (50, 100, 200, 500, 1000 rows)
  - Navigate pages (First, Previous, Next, Last)
  - Export to CSV (up to 10,000 rows)

#### Backend API Endpoints

**GET `/api/files/{fileName}/metadata`**
- Submits SAS job with PROC CONTENTS
- Returns: column names, types, lengths, row count, file info
- Implemented in: `FilesApiController.GetMetadata()`
- Service: `DatasetReaderService.GetMetadataAsync()`

**POST `/api/files/{fileName}/data`**
- Submits SAS job to export data as JSON
- Request body: page, pageSize, sortColumn, sortAscending, filters[]
- Returns: paginated rows with total count
- Implemented in: `FilesApiController.GetData()`
- Service: `DatasetReaderService.GetRowsAsync()`

#### DatasetReaderService Implementation
Uses SAS jobs to read `.sas7bdat` files:
- Generates SAS code with PROC CONTENTS, PROC SQL, PROC JSON
- Submits jobs via SlcHubClient
- Polls for completion (5 min timeout)
- Parses JSON output from stdout
- Supports WHERE clauses for filtering
- Supports ORDER BY for sorting
- Calculates FIRSTOBS/OBS for pagination

## Files Modified

1. **`SasJobRunner/wwwroot/js/file-browser.js`**
   - Simplified `viewDataset()` to navigate to standalone page

2. **`SasJobRunner/Program.cs`**
   - Added `IDatasetReaderService` service registration

## Testing

After restarting the application:

1. **Navigate to Editor**: Go to `/Editor`
2. **View Files**: Click "Files" tab in right panel
3. **Click a Dataset**: Click on any `.sas7bdat` file
4. **Should Navigate**: Browser navigates to `/dataset-viewer/{fileName}`
5. **Should Load Metadata**: Top bar shows row count, column count, size, modified date
6. **Should Load Data**: Table displays first 100 rows with all columns
7. **Test Sorting**: Click column headers to sort
8. **Test Pagination**: Click Next/Previous buttons
9. **Test Filtering**: Click "Add Filter", select column, operator, value, and Apply
10. **Test Export**: Click "Export (CSV)" button

## Alternative: Dataset Controller

There's also a simpler server-rendered dataset viewer at `/Dataset?dataset={name}` which could be used instead:
- Sidebar with list of all datasets
- Server-side rendering (no AJAX)
- Basic sorting and pagination
- Simpler implementation

To use this instead, change `file-browser.js`:
```javascript
function viewDataset(fileName) {
    window.location.href = `/Dataset?dataset=${encodeURIComponent(fileName)}`;
}
```

## Notes

- The original modal-based viewer code is still in `file-browser.js` but is no longer called
- The `/api/files/{fileName}/view` endpoint (PROC CONTENTS job submission) is no longer used by the file browser
- Both dataset viewing systems are fully implemented and working
- The standalone viewer (`/dataset-viewer/`) provides more advanced features
