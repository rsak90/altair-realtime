using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.SignalR;
using SasJobRunner.Hubs;
using SasJobRunner.Models;
using SasJobRunner.Services;

namespace SasJobRunner.Controllers.Api;

/// <summary>
/// API controller for working dataset file browsing and viewing.
/// GET /api/files — list all .sas7bdat files in session working directory
/// GET /api/files/{fileName}/metadata — get dataset metadata
/// POST /api/files/{fileName}/data — get dataset rows with filtering
/// POST /api/files/{fileName}/view — submit introspection job for a dataset
/// Requirements: 8.1, 8.2, 8.4, 8.10
/// </summary>
[ApiController]
[Route("api/files")]
public sealed class FilesApiController(
    ISessionJobOrchestrator orchestrator,
    IDatasetReaderService datasetReader,
    IConfiguration configuration,
    IHubContext<LogStreamingHub> hubContext,
    ILogger<FilesApiController> logger) : ControllerBase
{
    /// <summary>
    /// Lists all .sas7bdat dataset files in the session working directory
    /// with file size and last modified timestamp.
    /// Requirements: 8.1, 8.10
    /// </summary>
    [HttpGet]
    public IActionResult ListFiles()
    {
        var userId = HttpContext.Session.GetString("UserId");
        var sessionId = HttpContext.Session.GetString("SessionId");

        if (string.IsNullOrEmpty(userId) || string.IsNullOrEmpty(sessionId))
            return BadRequest("UserId or SessionId not in session.");

        // Use the configured StudyFolder to construct the correct path
        var studyFolder = configuration["SessionStorage:StudyFolder"] 
            ?? throw new InvalidOperationException("SessionStorage:StudyFolder configuration is required.");
        var workingDir = Path.Combine(studyFolder.TrimEnd('/'), "sessions", userId, sessionId);
        
        if (!Directory.Exists(workingDir))
            return Ok(Array.Empty<DatasetFileInfo>());

        try
        {
            var files = Directory.GetFiles(workingDir, "*.sas7bdat", SearchOption.TopDirectoryOnly)
                .Select(path =>
                {
                    var fileInfo = new FileInfo(path);
                    return new DatasetFileInfo(
                        Path.GetFileNameWithoutExtension(fileInfo.Name),
                        fileInfo.Length,
                        fileInfo.LastWriteTime
                    );
                })
                .OrderBy(f => f.Name)
                .ToList();

            return Ok(files);
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Error listing dataset files in {WorkingDir}", workingDir);
            return StatusCode(500, "Failed to list dataset files.");
        }
    }

    /// <summary>
    /// Gets metadata about a dataset including columns, row count, etc.
    /// </summary>
    [HttpGet("{fileName}/metadata")]
    public async Task<IActionResult> GetMetadata(
        string fileName,
        CancellationToken ct)
    {
        var userId = HttpContext.Session.GetString("UserId");
        var sessionId = HttpContext.Session.GetString("SessionId");

        if (string.IsNullOrEmpty(userId) || string.IsNullOrEmpty(sessionId))
            return BadRequest("UserId or SessionId not in session.");

        // Validate fileName to prevent injection
        if (!System.Text.RegularExpressions.Regex.IsMatch(fileName, @"^[A-Za-z_][A-Za-z0-9_]*$"))
            return BadRequest("Invalid dataset file name.");

        try
        {
            var metadata = await datasetReader.GetMetadataAsync(userId, sessionId, fileName, ct);
            return Ok(metadata);
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Error getting metadata for dataset {FileName}", fileName);
            return StatusCode(500, new { error = "Failed to get dataset metadata", detail = ex.Message });
        }
    }

    /// <summary>
    /// Gets dataset rows with optional filtering, sorting, and pagination
    /// </summary>
    [HttpPost("{fileName}/data")]
    public async Task<IActionResult> GetData(
        string fileName,
        [FromBody] DatasetFilterRequest request,
        CancellationToken ct)
    {
        var userId = HttpContext.Session.GetString("UserId");
        var sessionId = HttpContext.Session.GetString("SessionId");

        if (string.IsNullOrEmpty(userId) || string.IsNullOrEmpty(sessionId))
            return BadRequest("UserId or SessionId not in session.");

        // Validate fileName to prevent injection
        if (!System.Text.RegularExpressions.Regex.IsMatch(fileName, @"^[A-Za-z_][A-Za-z0-9_]*$"))
            return BadRequest("Invalid dataset file name.");

        try
        {
            var result = await datasetReader.GetRowsAsync(userId, sessionId, fileName, request, ct);
            return Ok(result);
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Error getting data for dataset {FileName}", fileName);
            return StatusCode(500, new { error = "Failed to get dataset data", detail = ex.Message });
        }
    }

    /// <summary>
    /// Submits a background job to introspect a dataset using PROC CONTENTS and PROC PRINT.
    /// Returns the jobId for SignalR subscription.
    /// Requirements: 8.2, 8.4
    /// </summary>
    [HttpPost("{fileName}/view")]
    public async Task<IActionResult> ViewDataset(
        string fileName,
        CancellationToken ct)
    {
        var userId = HttpContext.Session.GetString("UserId");
        var sessionId = HttpContext.Session.GetString("SessionId");

        if (string.IsNullOrEmpty(userId) || string.IsNullOrEmpty(sessionId))
            return BadRequest("UserId or SessionId not in session.");

        // Validate fileName to prevent injection
        if (!System.Text.RegularExpressions.Regex.IsMatch(fileName, @"^[A-Za-z_][A-Za-z0-9_]*$"))
            return BadRequest("Invalid dataset file name.");

        try
        {
            // Generate SAS code to introspect the dataset
            var sasCode = $@"
PROC CONTENTS DATA=SESSLIB.{fileName} SHORT;
RUN;

PROC PRINT DATA=SESSLIB.{fileName}(OBS=1000);
RUN;
";

            var jobId = await orchestrator.SubmitAsync(userId, sessionId, sasCode, ct);
            return Ok(new JobSubmitResponse(jobId));
        }
        catch (SlcHubException ex)
        {
            logger.LogWarning(ex, "SLC Hub returned non-success for dataset view.");
            return StatusCode(502, ex.Message);
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Error submitting dataset view job for {FileName}", fileName);
            return StatusCode(500, "Failed to submit dataset view job.");
        }
    }
}
