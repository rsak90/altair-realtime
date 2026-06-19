namespace SasJobRunner.Services;

public interface ISessionJobOrchestrator
{
    /// <summary>
    /// Assembles preamble + user code + trailer, submits to Hub,
    /// starts background log streaming, and returns the jobId.
    /// </summary>
    Task<string> SubmitAsync(
        string userId,
        string sessionId,
        string userSourceCode,
        CancellationToken ct = default);

    /// <summary>
    /// Submits a job using SLC system options for persistent WORK behavior.
    /// </summary>
    Task<string> SubmitWithPersistentWorkAsync(
        string userId,
        string sessionId,
        string userSourceCode,
        CancellationToken ct = default);
}
