using SasJobRunner.Models;

namespace SasJobRunner.Services;

/// <summary>
/// Typed HttpClient wrapper for the Altair SLC Hub REST API.
/// Every outbound request carries Authorization: Bearer {token} from ASP.NET Session.
/// </summary>
public interface ISlcHubClient
{
    /// <summary>
    /// Creates a job draft and returns the jobId.
    /// POST to /api/v2/namespaces/{namespace}/jobs with code and executionProfile.
    /// Throws <see cref="SlcHubException"/> on non-2xx response.
    /// </summary>
    Task<string> CreateJobAsync(string assembledCode, CancellationToken ct = default);

    /// <summary>
    /// Creates a job draft with SLC system options and returns the jobId.
    /// </summary>
    Task<string> CreateJobWithSystemOptionsAsync(
        string assembledCode,
        IReadOnlyList<SlcHubSystemOption> systemOptions,
        CancellationToken ct = default);

    /// <summary>
    /// Commits a job draft, starting execution.
    /// POST to /api/v2/namespaces/{namespace}/jobs/{jobId}/commit.
    /// Throws <see cref="SlcHubException"/> on non-2xx response.
    /// </summary>
    Task CommitJobAsync(string jobId, CancellationToken ct = default);

    /// <summary>
    /// Polls job status endpoint and returns the JobStatus string.
    /// GET /api/v2/namespaces/{namespace}/jobs/{jobId}.
    /// Throws <see cref="SlcHubException"/> on non-2xx response.
    /// </summary>
    Task<string> GetJobStatusAsync(string jobId, CancellationToken ct = default);

    /// <summary>
    /// Retrieves the full log content for a completed job.
    /// GET /api/v2/namespaces/{namespace}/jobs/{jobId}/log.
    /// Throws <see cref="SlcHubException"/> on non-2xx response.
    /// </summary>
    Task<string> GetJobLogAsync(string jobId, CancellationToken ct = default);

    /// <summary>
    /// Retrieves the result file listing for a completed job.
    /// GET /api/v2/namespaces/{namespace}/jobs/{jobId}/results.
    /// Returns empty list if no results are available.
    /// </summary>
    Task<IReadOnlyList<JobResultFile>> GetJobResultsAsync(string jobId, CancellationToken ct = default);

    /// <summary>
    /// Fetches the content of a result file by URL.
    /// Throws <see cref="SlcHubException"/> on non-2xx response.
    /// </summary>
    Task<string> GetResultFileContentAsync(string fileUrl, CancellationToken ct = default);

    /// <summary>
    /// Explicitly sets the Bearer token for scenarios where HttpContext is not available
    /// (e.g., background tasks, fire-and-forget operations).
    /// </summary>
    void SetBearerToken(string token);
}

/// <summary>
/// Represents a non-2xx response from the Altair SLC Hub.
/// </summary>
public sealed class SlcHubException(string message, int statusCode) : Exception(message)
{
    public int StatusCode { get; } = statusCode;
}
