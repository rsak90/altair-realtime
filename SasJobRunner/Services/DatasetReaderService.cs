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
PROC CONTENTS DATA=SESSLIB.{datasetName} OUT=_meta_ NOPRINT;
RUN;

DATA _NULL_;
    SET _meta_ END=eof;
    FILE STDOUT;
    IF _N_ = 1 THEN DO;
        PUT '{{""columns"":[';
    END;
    IF _N_ > 1 THEN PUT ',';
    PUT '{{""name"":""' NAME +(-1) '"",' ;
    PUT '""type"":""' TYPE +(-1) '"",' ;
    PUT '""length"":' LENGTH ',' ;
    PUT '""format"":""' FORMAT +(-1) '"",' ;
    PUT '""label"":""' LABEL +(-1) '""}}';
    IF eof THEN DO;
        PUT '],';
        PUT '""rowCount"":' NOBS ',';
        PUT '""columnCount"":' NVAR;
        PUT '}}';
    END;
RUN;
";

        var preamble = preambleBuilder.Build(userId, sessionId, new Dictionary<string, string>());
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
        
        // Parse JSON output
        var json = JsonSerializer.Deserialize<JsonElement>(stdoutContent);
        var columns = json.GetProperty("columns").EnumerateArray()
            .Select(col => new ColumnInfo(
                col.GetProperty("name").GetString() ?? "",
                col.GetProperty("type").GetString() ?? "",
                col.GetProperty("length").GetInt32(),
                col.TryGetProperty("format", out var fmt) ? fmt.GetString() : null,
                col.TryGetProperty("label", out var lbl) ? lbl.GetString() : null
            ))
            .ToList();

        var rowCount = json.GetProperty("rowCount").GetInt32();
        var columnCount = json.GetProperty("columnCount").GetInt32();

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
        
        // Build ORDER BY clause
        var orderByClause = request.SortColumn != null
            ? $"ORDER BY {request.SortColumn} {(request.SortAscending ? "ASC" : "DESC")}"
            : "";

        // Calculate start and end observation numbers
        var startObs = (request.Page - 1) * request.PageSize + 1;
        var endObs = request.Page * request.PageSize;

        // Generate SAS code to export data as JSON
        var sasCode = $@"
/* Count total rows matching filter */
PROC SQL NOPRINT;
    SELECT COUNT(*) INTO :total_rows
    FROM SESSLIB.{datasetName}
    {whereClause};
QUIT;

/* Export paginated data */
DATA _export_;
    SET SESSLIB.{datasetName}(FIRSTOBS={startObs} OBS={endObs});
    {whereClause.Replace("WHERE", "WHERE")}
    {orderByClause}
RUN;

/* Export as JSON */
FILENAME outjson TEMP;
PROC JSON OUT=outjson PRETTY;
    EXPORT _export_;
RUN;

/* Print JSON to stdout */
DATA _NULL_;
    INFILE outjson;
    FILE STDOUT;
    INPUT;
    PUT _INFILE_;
RUN;

/* Output total count */
DATA _NULL_;
    FILE STDOUT;
    PUT '{{""totalRows"":' &total_rows '}}';
RUN;
";

        var preamble = preambleBuilder.Build(userId, sessionId, new Dictionary<string, string>());
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
        
        // Parse JSON output
        var lines = stdoutContent.Split('\n', StringSplitOptions.RemoveEmptyEntries);
        var jsonData = string.Join("", lines.TakeWhile(l => !l.Contains("totalRows")));
        var totalLine = lines.FirstOrDefault(l => l.Contains("totalRows"));

        var dataJson = JsonSerializer.Deserialize<JsonElement>(jsonData);
        var rows = dataJson.EnumerateArray()
            .Select(row => {
                var cols = new Dictionary<string, string>();
                foreach (var prop in row.EnumerateObject())
                {
                    cols[prop.Name] = prop.Value.ToString();
                }
                return new DatasetRow(cols);
            })
            .ToList();

        var totalRows = 0;
        if (totalLine != null)
        {
            var totalJson = JsonSerializer.Deserialize<JsonElement>(totalLine);
            totalRows = totalJson.GetProperty("totalRows").GetInt32();
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
