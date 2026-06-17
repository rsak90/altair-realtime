namespace SasJobRunner.Services;

/// <summary>
/// Generates simple sequential session IDs instead of GUIDs for easier debugging.
/// Uses thread-safe counter to ensure unique IDs across concurrent requests.
/// </summary>
public sealed class SessionIdGenerator
{
    private static int _counter = 0;
    private static readonly object _lock = new object();

    /// <summary>
    /// Generates a new session ID in the format "session-{number}".
    /// Thread-safe and monotonically increasing.
    /// </summary>
    public static string Generate()
    {
        lock (_lock)
        {
            _counter++;
            return $"session-{_counter}";
        }
    }

    /// <summary>
    /// Resets the counter to zero. Only use for testing purposes.
    /// </summary>
    public static void Reset()
    {
        lock (_lock)
        {
            _counter = 0;
        }
    }
}
