using System.Text;
using System.Text.Json;
using SasJobRunner.Models;

namespace SasJobRunner.Services;

/// <summary>
/// Reads SAS dataset files by submitting SAS jobs to export data as JSON
/// </summary>
public sealed class DatasetReaderService(
    ISlcHubClient hubClient,
    PreambleBuilder preambleBuilder,
    IMacroProgramStore macroProgramStore,
    IConfiguration configuration,
    ILogger<DatasetReaderService> logger) : IDatasetReaderService
{
    public async Task<DatasetMetadata> GetMetadataAsync(
        string userId,
        string sessionId,
        string datasetName,
        CancellationToken ct = default)
    {
        // Generate SAS code to get metadata using PROC CONTENTS
        var sasCode = $@"
/* Get metadata using PROC CONTENTS */
PROC CONTENTS DATA=SESSLIB.{datasetName} OUT=_meta_ NOPRINT;
RUN;

/* Get row count and column count */
PROC SQL NOPRINT;
    SELECT NOBS, NVAR INTO :nobs TRIMMED, :nvar TRIMMED
    FROM _meta_(OBS=1);
QUIT;

/* Export column information as JSON */
FILENAME outjson STDOUT;

DATA _NULL_;
    SET _meta_;
    FILE STDOUT;
    
    IF _N_ = 1 THEN DO;
        PUT '[';
    END;
    ELSE DO;
        PUT ',';
    END;
    
    PUT '{{';
    PUT '""name"":""' NAME +(-1) '"",';
    PUT '""type"":""' TYPE +(-1) '"",';
    PUT '""length"":' LENGTH ',';
    PUT '""format"":""' FORMAT +(-1) '"",';
    PUT '""label"":""' LABEL +(-1) '""';
    PUT '}}';
RUN;

DATA _NULL_;
    FILE STDOUT;
    PUT ']';
    PUT '___METADATA___';
    PUT &nobs;
    PUT &nvar;
RUN;

/* Clean up */
PROC DELETE DATA=_meta_;
RUN;
";

        var macroPrograms = await macroProgramStore.GetAsync(sessionId);
        var preamble = preambleBuilder.Build(userId, sessionId, new Dictionary<string, string>(), macroPrograms);
        var fullCode = preamble + Environment.NewLine + sasCode;

        var jobId = await hubClient.CreateJobAsync(fullCode, ct);
        await hubClient.CommitJobAsync(jobId, ct);

        // Poll for completion
        var status = await PollJobStatusAsync(jobId, ct);
        
        if (status != "CompletedSuccess")
        {
            throw new InvalidOperationException($"Metadata job failed with status: {status}");
        }

        // Get stdout result
        var results = await hubClient.GetJobResultsAsync(jobId, ct);
        var stdoutFile = results.FirstOrDefault(f => f.Name.Equals("stdout", StringComparison.OrdinalIgnoreCase));
        
        if (stdoutFile == null)
        {
            throw new InvalidOperationException("No stdout file found in job results");
        }

        var stdoutContent = await hubClient.GetResultFileContentAsync(stdoutFile.Url, ct);
        
        // Parse the output - it contains JSON array followed by marker and metadata
        var markerIndex = stdoutContent.IndexOf("___METADATA___");
        
        if (markerIndex == -1)
        {
            throw new InvalidOperationException("Could not find metadata marker in stdout");
        }

        var jsonData = stdoutContent.Substring(0, markerIndex).Trim();
        var afterMarker = stdoutContent.Substring(markerIndex + "___METADATA___".Length).Trim();
        var metadataLines = afterMarker.Split('\n', StringSplitOptions.RemoveEmptyEntries);
        
        // Parse column information
        List<ColumnInfo> columns = new();
        
        if (!string.IsNullOrWhiteSpace(jsonData))
        {
            try
            {
                var columnsJson = JsonSerializer.Deserialize<JsonElement>(jsonData);
                
                if (columnsJson.ValueKind == JsonValueKind.Array)
                {
                    columns = columnsJson.EnumerateArray()
                        .Select(col => new ColumnInfo(
                            col.GetProperty("name").GetString() ?? "",
                            col.GetProperty("type").GetString() ?? "",
                            col.GetProperty("length").GetInt32(),
                            col.TryGetProperty("format", out var fmt) ? fmt.GetString() : null,
                            col.TryGetProperty("label", out var lbl) ? lbl.GetString() : null
                        ))
                        .ToList();
                }
            }
            catch (JsonException ex)
            {
                logger.LogError(ex, "Failed to parse metadata JSON. Content: {Content}", jsonData);
                throw new InvalidOperationException($"Failed to parse metadata JSON: {ex.Message}");
            }
        }

        // Parse row count and column count
        var rowCount = metadataLines.Length > 0 && int.TryParse(metadataLines[0].Trim(), out var rc) ? rc : 0;
        var columnCount = metadataLines.Length > 1 && int.TryParse(metadataLines[1].Trim(), out var cc) ? cc : columns.Count;

        // Get file info
        var studyFolder = configuration["SessionStorage:StudyFolder"] 
            ?? throw new InvalidOperationException("SessionStorage:StudyFolder configuration is required.");
        var filePath = Path.Combine(studyFolder.TrimEnd('/'), "sessions", userId, sessionId, $"{datasetName}.sas7bdat");
        var fileInfo = new FileInfo(filePath);

        return new DatasetMetadata(
            datasetName,
            rowCount,
            columnCount,
            columns,
            fileInfo.Length,
            fileInfo.LastWriteTime
        );
    }

    public async Task<PagedResult<DatasetRow>> GetRowsAsync(
        string userId,
        string sessionId,
        string datasetName,
        DatasetFilterRequest request,
        CancellationToken ct = default)
    {
        // Build WHERE clause from filters
        var whereClause = BuildWhereClause(request.Filters);
        
        // Build ORDER BY clause for PROC SQL
        var orderByClause = request.SortColumn != null
            ? $"ORDER BY {request.SortColumn} {(request.SortAscending ? "" : "DESC")}"
            : "";

        // Calculate start and end observation numbers
        var startObs = (request.Page - 1) * request.PageSize + 1;
        var endObs = request.Page * request.PageSize;

        // Generate SAS code to export data as JSON
        var sasCode = $@"
/* Count total rows matching filter */
PROC SQL NOPRINT;
    SELECT COUNT(*) INTO :total_rows TRIMMED
    FROM SESSLIB.{datasetName}
    {whereClause};
QUIT;

/* Create filtered and sorted dataset */
PROC SQL;
    CREATE TABLE _export_ AS
    SELECT *
    FROM SESSLIB.{datasetName}
    {whereClause}
    {orderByClause};
QUIT;

/* Apply pagination */
DATA _export_page_;
    SET _export_(FIRSTOBS={startObs} OBS={endObs});
RUN;

/* Export as JSON to stdout */
FILENAME outjson STDOUT;
PROC JSON OUT=outjson;
    EXPORT _export_page_;
RUN;

/* Output marker */
DATA _NULL_;
    FILE STDOUT;
    PUT '___TOTAL_ROWS___';
RUN;

/* Output total count */
DATA _NULL_;
    FILE STDOUT;
    PUT &total_rows;
RUN;

/* Clean up */
PROC DELETE DATA=_export_;
RUN;
PROC DELETE DATA=_export_page_;
RUN;
";

        var macroPrograms = await macroProgramStore.GetAsync(sessionId);
        var preamble = preambleBuilder.Build(userId, sessionId, new Dictionary<string, string>(), macroPrograms);
        var fullCode = preamble + Environment.NewLine + sasCode;

        var jobId = await hubClient.CreateJobAsync(fullCode, ct);
        await hubClient.CommitJobAsync(jobId, ct);

        // Poll for completion
        var status = await PollJobStatusAsync(jobId, ct);
        
        if (status != "CompletedSuccess")
        {
            throw new InvalidOperationException($"Data export job failed with status: {status}");
        }

        // Get stdout result
        var results = await hubClient.GetJobResultsAsync(jobId, ct);
        var stdoutFile = results.FirstOrDefault(f => f.Name.Equals("stdout", StringComparison.OrdinalIgnoreCase));
        
        if (stdoutFile == null)
        {
            throw new InvalidOperationException("No stdout file found in job results");
        }

        var stdoutContent = await hubClient.GetResultFileContentAsync(stdoutFile.Url, ct);
        
        // Parse the output - it contains JSON array followed by marker and total count
        var markerIndex = stdoutContent.IndexOf("___TOTAL_ROWS___");
        
        if (markerIndex == -1)
        {
            throw new InvalidOperationException("Could not find total rows marker in stdout");
        }

        var jsonData = stdoutContent.Substring(0, markerIndex).Trim();
        var afterMarker = stdoutContent.Substring(markerIndex + "___TOTAL_ROWS___".Length).Trim();
        
        // Parse total rows
        var totalRows = 0;
        var totalRowsMatch = System.Text.RegularExpressions.Regex.Match(afterMarker, @"^\s*(\d+)");
        if (totalRowsMatch.Success)
        {
            totalRows = int.Parse(totalRowsMatch.Groups[1].Value);
        }

        // Parse JSON data
        List<DatasetRow> rows = new();
        
        if (!string.IsNullOrWhiteSpace(jsonData))
        {
            try
            {
                // PROC JSON exports as an array of objects
                var dataJson = JsonSerializer.Deserialize<JsonElement>(jsonData);
                
                if (dataJson.ValueKind == JsonValueKind.Array)
                {
                    rows = dataJson.EnumerateArray()
                        .Select(row => {
                            var cols = new Dictionary<string, string>();
                            foreach (var prop in row.EnumerateObject())
                            {
                                cols[prop.Name] = prop.Value.ToString();
                            }
                            return new DatasetRow(cols);
                        })
                        .ToList();
                }
            }
            catch (JsonException ex)
            {
                logger.LogError(ex, "Failed to parse JSON from stdout. Content: {Content}", jsonData);
                throw new InvalidOperationException($"Failed to parse dataset JSON: {ex.Message}");
            }
        }

        return new PagedResult<DatasetRow>(rows, totalRows, request.Page, request.PageSize);
    }

    private string BuildWhereClause(IReadOnlyList<ColumnFilter>? filters)
    {
        if (filters == null || filters.Count == 0)
            return "";

        var conditions = new List<string>();

        foreach (var filter in filters)
        {
            var condition = filter.Operator.ToLower() switch
            {
                "equals" => $"{filter.ColumnName} = '{EscapeSql(filter.Value)}'",
                "contains" => $"INDEX(UPCASE({filter.ColumnName}), UPCASE('{EscapeSql(filter.Value)}')) > 0",
                "startswith" => $"UPCASE({filter.ColumnName}) LIKE UPCASE('{EscapeSql(filter.Value)}%')",
                "endswith" => $"UPCASE({filter.ColumnName}) LIKE UPCASE('%{EscapeSql(filter.Value)}')",
                "gt" => $"{filter.ColumnName} > {filter.Value}",
                "lt" => $"{filter.ColumnName} < {filter.Value}",
                "gte" => $"{filter.ColumnName} >= {filter.Value}",
                "lte" => $"{filter.ColumnName} <= {filter.Value}",
                _ => null
            };

            if (condition != null)
                conditions.Add(condition);
        }

        return conditions.Count > 0 ? $"WHERE {string.Join(" AND ", conditions)}" : "";
    }

    private string EscapeSql(string value)
    {
        return value.Replace("'", "''");
    }

    private async Task<string> PollJobStatusAsync(string jobId, CancellationToken ct)
    {
        var maxWaitTime = TimeSpan.FromMinutes(5);
        var pollInterval = TimeSpan.FromSeconds(2);
        var startTime = DateTime.UtcNow;
        string status = "Running";

        while (status != "CompletedSuccess" && status != "CompletedError" && status != "Failed")
        {
            if (DateTime.UtcNow - startTime > maxWaitTime)
            {
                throw new TimeoutException($"Job {jobId} timed out after {maxWaitTime}");
            }
            await Task.Delay(pollInterval, ct);
            status = await hubClient.GetJobStatusAsync(jobId, ct);
        }

        return status;
    }
}
