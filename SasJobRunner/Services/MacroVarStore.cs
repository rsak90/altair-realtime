using System.Collections.Concurrent;
using System.Text.Json;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;
using SasJobRunner.Models;

namespace SasJobRunner.Services;

public sealed class MacroVarStore : IMacroVarStore
{
    private readonly ConcurrentDictionary<string, Dictionary<string, string>> _store = new();
    private readonly ConcurrentDictionary<string, bool> _loadedFromDisk = new();
    private readonly ConcurrentDictionary<string, SemaphoreSlim> _fileLocks = new();
    private readonly ConcurrentDictionary<string, string> _sessionToUser = new();
    private readonly IConfiguration _configuration;
    private readonly ILogger<MacroVarStore> _logger;
    private readonly string? _studyFolder;

    public MacroVarStore(IConfiguration configuration, ILogger<MacroVarStore> logger)
    {
        _configuration = configuration;
        _logger = logger;
        
        // Read SessionStorage:StudyFolder configuration
        _studyFolder = _configuration["SessionStorage:StudyFolder"];
        
        if (string.IsNullOrWhiteSpace(_studyFolder))
        {
            _logger.LogError("SessionStorage:StudyFolder configuration is missing. MacroVarStore will operate in in-memory-only mode without persistence.");
            _logger.LogWarning("Macro variables will not persist across application restarts. Configure SessionStorage:StudyFolder to enable persistence.");
        }
        else
        {
            _logger.LogInformation("MacroVarStore initialized with StudyFolder: {StudyFolder}. Macro variable persistence is enabled.", _studyFolder);
        }
    }

    public async Task<IReadOnlyDictionary<string, string>> GetAsync(string sessionId)
    {
        // Check if session exists in cache - if yes, return immediately (cache-first)
        if (_store.TryGetValue(sessionId, out var cachedVars))
        {
            return cachedVars;
        }

        // Check if session is marked as loaded from disk - if yes, return empty
        // This prevents repeated file access attempts for sessions with no persisted variables
        if (_loadedFromDisk.ContainsKey(sessionId))
        {
            return new Dictionary<string, string>();
        }

        // Session not in cache and not yet loaded from disk - attempt lazy load
        var loadedVars = await LoadFromFileAsync(sessionId);

        // Populate cache with loaded variables (even if empty)
        _store.TryAdd(sessionId, loadedVars);

        // Mark session as loaded to prevent repeated file reads
        _loadedFromDisk.TryAdd(sessionId, true);

        return loadedVars;
    }

    public Task SetAsync(string sessionId, IReadOnlyDictionary<string, string> vars)
    {
        // Update in-memory cache immediately (synchronous)
        var variableCopy = new Dictionary<string, string>(vars);
        _store[sessionId] = variableCopy;

        // Try to resolve userId from cache or file system (Req 8.3)
        // TryResolveUserId will cache the sessionId-to-userId mapping in _sessionToUser
        var userId = TryResolveUserId(sessionId);
        
        if (userId != null)
        {
            // Fire-and-forget background write to file (async)
            _ = Task.Run(async () =>
            {
                await WriteToFileAsync(sessionId, userId, variableCopy);
            });
        }
        else
        {
            // Log warning if userId cannot be determined (Req 8.3)
            _logger.LogWarning("Skipping file write for session {SessionId}: userId could not be resolved. Macro variables will remain in memory only.", sessionId);
        }

        // Return immediately without awaiting file write
        return Task.CompletedTask;
    }

    public Task SetVarAsync(string sessionId, string name, string value)
    {
        // Atomically update single variable in the cache
        var updatedVars = _store.AddOrUpdate(sessionId,
            _ => new Dictionary<string, string> { [name] = value },
            (_, existing) => { existing[name] = value; return existing; });

        // Try to resolve userId from cache or file system (Req 8.3)
        // TryResolveUserId will cache the sessionId-to-userId mapping in _sessionToUser
        var userId = TryResolveUserId(sessionId);
        if (userId != null)
        {
            // Fire-and-forget: don't await the file write
            _ = Task.Run(async () => await WriteToFileAsync(sessionId, userId, updatedVars));
        }
        else
        {
            // Log warning if userId cannot be determined (Req 8.3)
            _logger.LogWarning("Skipping file write for session {SessionId}: userId could not be resolved. Macro variables will remain in memory only.", sessionId);
        }

        // Return immediately without waiting for file write
        return Task.CompletedTask;
    }

    /// <summary>
    /// Registers a session with its associated userId for path construction.
    /// This method should be called by SessionJobOrchestrator before first access to ensure
    /// the userId is available for file operations without needing a filesystem scan.
    /// </summary>
    /// <param name="sessionId">The session ID to register</param>
    /// <param name="userId">The user ID who owns the session</param>
    internal void RegisterSession(string sessionId, string userId)
    {
        if (string.IsNullOrWhiteSpace(sessionId))
        {
            throw new ArgumentException("Session ID cannot be null or empty", nameof(sessionId));
        }
        
        if (string.IsNullOrWhiteSpace(userId))
        {
            throw new ArgumentException("User ID cannot be null or empty", nameof(userId));
        }
        
        _sessionToUser.TryAdd(sessionId, userId);
        _logger.LogDebug("Registered session {SessionId} for user {UserId}", sessionId, userId);
    }

    /// <summary>
    /// Attempts to resolve the userId for a given sessionId.
    /// First checks the in-memory cache, then scans the sessions directory structure if needed.
    /// </summary>
    /// <param name="sessionId">The session ID to resolve</param>
    /// <returns>The userId if found, otherwise null</returns>
    private string? TryResolveUserId(string sessionId)
    {
        // Check in-memory cache first
        if (_sessionToUser.TryGetValue(sessionId, out var cachedUserId))
        {
            return cachedUserId;
        }

        // If StudyFolder is not configured, cannot resolve from file system
        if (string.IsNullOrWhiteSpace(_studyFolder))
        {
            _logger.LogDebug("Cannot resolve userId for session {SessionId}: StudyFolder is not configured", sessionId);
            return null;
        }

        // Scan sessions directory to find the sessionId
        try
        {
            var sessionsPath = Path.Combine(_studyFolder, "sessions");
            
            if (!Directory.Exists(sessionsPath))
            {
                _logger.LogDebug("Sessions directory does not exist: {SessionsPath}", sessionsPath);
                return null;
            }

            // Iterate through user directories
            foreach (var userDirectory in Directory.GetDirectories(sessionsPath))
            {
                var userId = Path.GetFileName(userDirectory);
                var sessionPath = Path.Combine(userDirectory, sessionId);
                
                if (Directory.Exists(sessionPath))
                {
                    // Found the session, cache the mapping
                    _sessionToUser.TryAdd(sessionId, userId);
                    _logger.LogDebug("Resolved userId {UserId} for session {SessionId}", userId, sessionId);
                    return userId;
                }
            }

            _logger.LogDebug("Could not find userId for session {SessionId} in sessions directory", sessionId);
            return null;
        }
        catch (UnauthorizedAccessException ex)
        {
            _logger.LogWarning(ex, "Access denied while trying to resolve userId for session {SessionId}", sessionId);
            return null;
        }
        catch (IOException ex)
        {
            _logger.LogWarning(ex, "I/O error while trying to resolve userId for session {SessionId}", sessionId);
            return null;
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Unexpected error while trying to resolve userId for session {SessionId}", sessionId);
            return null;
        }
    }
    /// <summary>
    /// Gets or creates a file lock (SemaphoreSlim) for the specified session.
    /// This ensures thread-safe file operations per session.
    /// </summary>
    /// <param name="sessionId">The session ID to get the lock for</param>
    /// <returns>A SemaphoreSlim instance for the session</returns>
    private SemaphoreSlim GetFileLock(string sessionId)
    {
        return _fileLocks.GetOrAdd(sessionId, _ => new SemaphoreSlim(1, 1));
    }

    private string GetVariablesFilePath(string sessionId, string userId)
    {
        if (string.IsNullOrEmpty(_studyFolder))
        {
            throw new InvalidOperationException("StudyFolder configuration is required for persistence");
        }

        return Path.Combine(_studyFolder, "sessions", userId, sessionId, "variables.json");
    }

    /// <summary>
    /// Writes macro variables to the variables.json file for a given session.
    /// Uses atomic write pattern (write to temp file, then rename) to prevent corruption.
    /// This method is thread-safe per session using file locks.
    /// </summary>
    /// <param name="sessionId">The session ID to write variables for</param>
    /// <param name="userId">The user ID who owns the session</param>
    /// <param name="variables">The macro variables to persist</param>
    private async Task WriteToFileAsync(string sessionId, string userId, Dictionary<string, string> variables)
    {
        // Early exit if StudyFolder is not configured
        if (string.IsNullOrWhiteSpace(_studyFolder))
        {
            _logger.LogDebug("Skipping file write for session {SessionId}: StudyFolder is not configured", sessionId);
            return;
        }

        // Get the file lock for this session
        var fileLock = GetFileLock(sessionId);
        
        await fileLock.WaitAsync();
        try
        {
            // Construct the file path
            string filePath = GetVariablesFilePath(sessionId, userId);
            string sessionDirectory = Path.GetDirectoryName(filePath)!;
            
            // Ensure the session directory exists
            if (!Directory.Exists(sessionDirectory))
            {
                Directory.CreateDirectory(sessionDirectory);
                _logger.LogDebug("Created session directory: {SessionDirectory}", sessionDirectory);
            }

            // Construct MacroVarFile with metadata and variables
            var macroVarFile = new MacroVarFile
            {
                Metadata = new MacroVarMetadata
                {
                    UserId = userId,
                    LastUpdated = DateTime.UtcNow
                },
                Variables = variables
            };

            // Serialize to JSON with indented formatting
            var jsonOptions = new JsonSerializerOptions
            {
                WriteIndented = true
            };
            var jsonContent = JsonSerializer.Serialize(macroVarFile, jsonOptions);

            // Write to temporary file first (atomic write pattern)
            var tempFilePath = Path.Combine(sessionDirectory, $"variables.{Guid.NewGuid()}.tmp");
            await File.WriteAllTextAsync(tempFilePath, jsonContent);

            // Atomically rename to target file (overwrite if exists)
            File.Move(tempFilePath, filePath, overwrite: true);

            // Task 8.2: Log success with structured properties (Req 9.1)
            _logger.LogInformation(
                "Successfully wrote {VariableCount} macro variables for session {SessionId} (user {UserId}) to {FilePath}",
                variables.Count, sessionId, userId, filePath);
        }
        // Task 8.2: Catch IOException separately with structured logging (Req 6.4, 6.5, 9.1, 9.3)
        catch (IOException ex)
        {
            _logger.LogWarning(ex, 
                "I/O error when writing variables file for session {SessionId} (user {UserId}) at {FilePath}: {Message}. Operation continues with in-memory cache.",
                sessionId, userId, GetVariablesFilePath(sessionId, userId), ex.Message);
        }
        // Task 8.2: Catch UnauthorizedAccessException separately with structured logging (Req 6.4, 6.5, 9.1, 9.3)
        catch (UnauthorizedAccessException ex)
        {
            _logger.LogWarning(ex,
                "Access denied when writing variables file for session {SessionId} (user {UserId}) at {FilePath}. Check file permissions. Operation continues with in-memory cache.",
                sessionId, userId, GetVariablesFilePath(sessionId, userId));
        }
        // Task 8.2: Catch general exceptions separately with structured logging (Req 6.4, 6.5, 9.1, 9.3)
        catch (Exception ex)
        {
            _logger.LogWarning(ex,
                "Unexpected error when writing variables file for session {SessionId} (user {UserId}) at {FilePath}: {Message}. Operation continues with in-memory cache.",
                sessionId, userId, GetVariablesFilePath(sessionId, userId), ex.Message);
        }
        finally
        {
            // Always release the file lock
            fileLock.Release();
        }
    }

    /// <summary>
    /// Loads macro variables from the variables.json file for a given session.
    /// This method handles file reading, deserialization, validation, and error handling.
    /// </summary>
    /// <param name="sessionId">The session ID to load variables for</param>
    /// <returns>A dictionary of macro variables, or an empty dictionary if the file doesn't exist or an error occurs</returns>
    private async Task<Dictionary<string, string>> LoadFromFileAsync(string sessionId)
    {
        // Try to resolve the userId needed for path construction
        var userId = TryResolveUserId(sessionId);
        
        if (userId == null)
        {
            _logger.LogDebug("Cannot load variables for session {SessionId}: userId could not be resolved", sessionId);
            return new Dictionary<string, string>();
        }

        // Construct the file path
        string filePath;
        try
        {
            filePath = GetVariablesFilePath(sessionId, userId);
        }
        catch (InvalidOperationException ex)
        {
            _logger.LogWarning(ex, "Cannot load variables for session {SessionId}: {Message}", sessionId, ex.Message);
            return new Dictionary<string, string>();
        }

        // Check if the file exists
        if (!File.Exists(filePath))
        {
            // Task 8.3: FileNotFoundException - log at debug level with structured properties (Req 9.4)
            _logger.LogDebug("Variables file does not exist for session {SessionId} (user {UserId}) at path {FilePath}. This is expected for new sessions.", 
                sessionId, userId, filePath);
            return new Dictionary<string, string>();
        }

        // Try to read and deserialize the file
        try
        {
            var jsonContent = await File.ReadAllTextAsync(filePath);
            
            // Task 8.1: Parse JSON with comprehensive validation
            MacroVarFile? macroVarFile;
            try
            {
                macroVarFile = JsonSerializer.Deserialize<MacroVarFile>(jsonContent);
            }
            catch (JsonException ex)
            {
                // Task 8.3: JsonException - log at warning level with structured properties (Req 9.2, 9.3)
                _logger.LogWarning(ex, 
                    "Failed to parse JSON in variables file for session {SessionId} (user {UserId}) at {FilePath}: {Message}. JSON validation error.", 
                    sessionId, userId, filePath, ex.Message);
                return new Dictionary<string, string>();
            }

            // Task 8.1: Validate root element is object type (Req 7.1)
            if (macroVarFile == null)
            {
                _logger.LogWarning(
                    "Variables file for session {SessionId} (user {UserId}) at {FilePath} has invalid structure: root element is not an object or deserialization returned null. Expected JSON object with metadata and variables sections.",
                    sessionId, userId, filePath);
                return new Dictionary<string, string>();
            }

            // Task 8.1: Verify metadata section exists with userId string (Req 7.2)
            if (macroVarFile.Metadata == null || string.IsNullOrWhiteSpace(macroVarFile.Metadata.UserId))
            {
                _logger.LogWarning(
                    "Variables file for session {SessionId} (user {UserId}) at {FilePath} has invalid metadata: metadata section is missing or userId is empty. Expected metadata object with userId string property.",
                    sessionId, userId, filePath);
                // Continue anyway - we can still use the variables (fail-safe)
            }

            // Task 8.1: Verify variables section is dictionary with string keys and values (Req 7.3)
            if (macroVarFile.Variables == null)
            {
                _logger.LogWarning(
                    "Variables file for session {SessionId} (user {UserId}) at {FilePath} has invalid structure: variables section is null. Expected dictionary with string keys and values.",
                    sessionId, userId, filePath);
                return new Dictionary<string, string>();
            }

            // Task 8.1: Skip invalid entries and continue with valid ones (Req 7.4)
            // Task 8.1: Log warnings with details for validation failures (Req 7.5)
            var validVariables = new Dictionary<string, string>();
            var skippedCount = 0;
            
            foreach (var kvp in macroVarFile.Variables)
            {
                if (string.IsNullOrEmpty(kvp.Key))
                {
                    _logger.LogWarning(
                        "Skipping variable with null or empty key in session {SessionId} (user {UserId}) at {FilePath}. Variable keys must be non-empty strings.",
                        sessionId, userId, filePath);
                    skippedCount++;
                    continue;
                }

                if (kvp.Value == null)
                {
                    _logger.LogWarning(
                        "Skipping variable '{VariableName}' with null value in session {SessionId} (user {UserId}) at {FilePath}. Variable values must be strings.",
                        kvp.Key, sessionId, userId, filePath);
                    skippedCount++;
                    continue;
                }

                validVariables[kvp.Key] = kvp.Value;
            }

            if (skippedCount > 0)
            {
                _logger.LogWarning(
                    "Skipped {SkippedCount} invalid variable entries in session {SessionId} (user {UserId}) at {FilePath}. Loaded {ValidCount} valid variables.",
                    skippedCount, sessionId, userId, filePath, validVariables.Count);
            }

            // Cache the userId from metadata for future use if it's valid
            if (macroVarFile.Metadata != null && !string.IsNullOrWhiteSpace(macroVarFile.Metadata.UserId))
            {
                _sessionToUser.TryAdd(sessionId, macroVarFile.Metadata.UserId);
            }

            _logger.LogDebug(
                "Successfully loaded {VariableCount} variables for session {SessionId} (user {UserId}) from {FilePath}", 
                validVariables.Count, sessionId, userId, filePath);
            return validVariables;
        }
        catch (FileNotFoundException ex)
        {
            // Task 8.3: FileNotFoundException - log at debug level (Req 9.4)
            _logger.LogDebug(ex,
                "Variables file not found for session {SessionId} (user {UserId}) at {FilePath}. This is expected for new sessions.",
                sessionId, userId, filePath);
            return new Dictionary<string, string>();
        }
        catch (JsonException ex)
        {
            // Task 8.3: JsonException - log at warning level with structured properties (Req 9.2, 9.3)
            _logger.LogWarning(ex, 
                "Failed to parse JSON in variables file for session {SessionId} (user {UserId}) at {FilePath}: {Message}. File may be corrupted or manually edited.",
                sessionId, userId, filePath, ex.Message);
            return new Dictionary<string, string>();
        }
        catch (UnauthorizedAccessException ex)
        {
            // Task 8.3: Other errors - log at warning level with structured properties (Req 9.2, 9.3)
            _logger.LogWarning(ex, 
                "Access denied when reading variables file for session {SessionId} (user {UserId}) at {FilePath}. Check file permissions.",
                sessionId, userId, filePath);
            return new Dictionary<string, string>();
        }
        catch (IOException ex)
        {
            // Task 8.3: Other errors - log at warning level with structured properties (Req 9.2, 9.3)
            _logger.LogWarning(ex, 
                "I/O error when reading variables file for session {SessionId} (user {UserId}) at {FilePath}: {Message}",
                sessionId, userId, filePath, ex.Message);
            return new Dictionary<string, string>();
        }
        catch (Exception ex)
        {
            // Task 8.3: Other errors - log at warning level with structured properties (Req 9.2, 9.3)
            _logger.LogWarning(ex, 
                "Unexpected error when loading variables for session {SessionId} (user {UserId}) from {FilePath}: {Message}",
                sessionId, userId, filePath, ex.Message);
            return new Dictionary<string, string>();
        }
    }
}
