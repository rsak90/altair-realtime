namespace SasJobRunner.Services;

public interface IMacroProgramStore
{
    /// <summary>
    /// Retrieves all macro program source code for a session.
    /// Returns cached source if available, otherwise loads from disk.
    /// </summary>
    /// <param name="sessionId">The session identifier</param>
    /// <returns>Complete macro source code (all macros concatenated), or empty string if none exist</returns>
    Task<string> GetAsync(string sessionId);

    /// <summary>
    /// Updates all macro program definitions for a session.
    /// Updates in-memory cache immediately and queues background file write.
    /// </summary>
    /// <param name="sessionId">The session identifier</param>
    /// <param name="macros">Dictionary mapping macro names to source code</param>
    Task SetAsync(string sessionId, IReadOnlyDictionary<string, string> macros);
}
