using System.Net.Http.Headers;
using System.Net.Http.Json;
using SasJobRunner.Models;

namespace SasJobRunner.Services;

/// <summary>
/// Typed HttpClient implementation of <see cref="ISlcHubClient"/>.
/// Registered via AddHttpClient&lt;ISlcHubClient, SlcHubClient&gt;.
/// </summary>
public sealed class SlcHubClient(
    HttpClient httpClient,
    IHttpContextAccessor contextAccessor,
    IConfiguration configuration,
    ILogger<SlcHubClient> logger) : ISlcHubClient
{
    /// <summary>
    /// Reads the Bearer token from the current ASP.NET Session and attaches it
    /// to the Authorization header of every outbound Hub request.
    /// </summary>
    private void ApplyBearerToken()
    {
        var token = contextAccessor.HttpContext!.Session.GetString("BearerToken")
            ?? throw new InvalidOperationException("Bearer token not found in session.");
        httpClient.DefaultRequestHeaders.Authorization =
            new AuthenticationHeaderValue("Bearer", token);
    }

    /// <inheritdoc/>
    public async Task<string> CreateJobAsync(string assembledCode, CancellationToken ct = default)
    {
        ApplyBearerToken();
        var ns = configuration["SlcHub:Namespace"]!;
        var profile = configuration["SlcHub:ExecutionProfile"]!;
        var response = await httpClient.PostAsJsonAsync(
            $"/api/v2/namespaces/{ns}/jobs",
            new { code = assembledCode, executionProfile = profile },
            ct);
        if (!response.IsSuccessStatusCode)
        {
            var body = await response.Content.ReadAsStringAsync(ct);
            throw new SlcHubException(body, (int)response.StatusCode);
        }
        var result = await response.Content.ReadFromJsonAsync<JobIdResult>(ct);
        return result!.JobId;
    }

    /// <inheritdoc/>
    public async Task CommitJobAsync(string jobId, CancellationToken ct = default)
    {
        ApplyBearerToken();
        var ns = configuration["SlcHub:Namespace"]!;
        var response = await httpClient.PostAsync(
            $"/api/v2/namespaces/{ns}/jobs/{jobId}/commit",
            null,
            ct);
        if (!response.IsSuccessStatusCode)
        {
            var body = await response.Content.ReadAsStringAsync(ct);
            throw new SlcHubException(body, (int)response.StatusCode);
        }
    }

    /// <inheritdoc/>
    public async Task<string> GetJobStatusAsync(string jobId, CancellationToken ct = default)
    {
        ApplyBearerToken();
        var ns = configuration["SlcHub:Namespace"]!;
        var response = await httpClient.GetAsync(
            $"/api/v2/namespaces/{ns}/jobs/{jobId}",
            ct);
        if (!response.IsSuccessStatusCode)
        {
            var body = await response.Content.ReadAsStringAsync(ct);
            throw new SlcHubException(body, (int)response.StatusCode);
        }
        var result = await response.Content.ReadFromJsonAsync<JobStatusResult>(ct);
        return result!.Status;
    }

    /// <inheritdoc/>
    public async Task<string> GetJobLogAsync(string jobId, CancellationToken ct = default)
    {
        ApplyBearerToken();
        var ns = configuration["SlcHub:Namespace"]!;
        var response = await httpClient.GetAsync(
            $"/api/v2/namespaces/{ns}/jobs/{jobId}/log",
            ct);
        if (!response.IsSuccessStatusCode)
        {
            var body = await response.Content.ReadAsStringAsync(ct);
            throw new SlcHubException(body, (int)response.StatusCode);
        }
        return await response.Content.ReadAsStringAsync(ct);
    }

    /// <inheritdoc/>
    public async Task<IReadOnlyList<JobResultFile>> GetJobResultsAsync(string jobId, CancellationToken ct = default)
    {
        ApplyBearerToken();
        var ns = configuration["SlcHub:Namespace"]!;
        var response = await httpClient.GetAsync(
            $"/api/v2/namespaces/{ns}/jobs/{jobId}/results",
            ct);
        if (!response.IsSuccessStatusCode)
            return Array.Empty<JobResultFile>();
        var result = await response.Content.ReadFromJsonAsync<ResultsResponse>(ct);
        return result?.Files ?? Array.Empty<JobResultFile>();
    }

    /// <inheritdoc/>
    public async Task<string> GetResultFileContentAsync(string fileUrl, CancellationToken ct = default)
    {
        ApplyBearerToken();
        var response = await httpClient.GetAsync(fileUrl, ct);
        if (!response.IsSuccessStatusCode)
        {
            var body = await response.Content.ReadAsStringAsync(ct);
            throw new SlcHubException(body, (int)response.StatusCode);
        }
        return await response.Content.ReadAsStringAsync(ct);
    }

    private record JobIdResult(string JobId);
    private record JobStatusResult(string Status);
    private record ResultsResponse(IReadOnlyList<JobResultFile> Files);
}
