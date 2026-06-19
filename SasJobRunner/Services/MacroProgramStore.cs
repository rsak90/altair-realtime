using System.Collections.Concurrent;
using System.Text;
using System.Text.RegularExpressions;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;

namespace SasJobRunner.Services;

public sealed class MacroProgramStore : IMacroProgramStore
{
    private static readonly Regex MacroNamePattern =
        new(@"^[A-Z_][A-Z0-9_]*$", RegexOptions.Compiled | RegexOptions.IgnoreCase);

    private readonly ConcurrentDictionary<string, string> _cache = new();
    private readonly ConcurrentDictionary<string, bool> _loadedFromDisk = new();
    private readonly ConcurrentDictionary<string, SemaphoreSlim> _fileLocks = new();
    private readonly ConcurrentDictionary<string, string> _sessionToUser = new();
    private readonly IConfiguration _configuration;
    private readonly ILogger<MacroProgramStore> _logger;
    private readonly string? _studyFolder;
    private readonly bool _memoryOnly;

    public MacroProgramStore(IConfiguration configuration, ILogger<MacroProgramStore> logger)
    {
        _configuration = configuration;
        _logger = logger;

        _studyFolder = _configuration["SessionStorage:StudyFolder"];
        _memoryOnly = string.IsNullOrWhiteSpace(_studyFolder);

        if (_memoryOnly)
        {
            _logger.LogError("SessionStorage:StudyFolder configuration is missing. MacroProgramStore will operate in in-memory-only mode without persistence.");
            _logger.LogWarning("Macro programs will not persist across application restarts. Configure SessionStorage:StudyFolder to enable persistence.");
        }
        else
        {
            _logger.LogInformation("MacroProgramStore initialized with StudyFolder: {StudyFolder}. Macro program persistence is enabled.", _studyFolder);
        }
    }

    public async Task<string> GetAsync(string sessionId)
    {
        if (_cache.TryGetValue(sessionId, out var cachedSource))
        {
            _logger.LogDebug("Cache hit for session {SessionId}", sessionId);
            return cachedSource;
        }

        if (_loadedFromDisk.ContainsKey(sessionId))
        {
            _logger.LogDebug("Session {SessionId} already loaded from disk (no macros)", sessionId);
            return string.Empty;
        }

        var loadedSource = await LoadFromFileAsync(sessionId);
        _cache.TryAdd(sessionId, loadedSource);
        _loadedFromDisk.TryAdd(sessionId, true);

        return loadedSource;
    }

    public Task SetAsync(string sessionId, IReadOnlyDictionary<string, string> macros)
    {
        var userId = TryResolveUserId(sessionId);
        var macroSource = FormatMacroSource(sessionId, userId, macros);
        var macroCount = CountMacroDefinitions(macroSource);

        _cache[sessionId] = macroSource;
        _loadedFromDisk.TryAdd(sessionId, true);

        _logger.LogDebug(
            "SetAsync called for session {SessionId} with {Count} macros ({ValidCount} valid in formatted output)",
            sessionId, macros.Count, macroCount);

        if (_memoryOnly)
        {
            _logger.LogDebug("Skipping file write for session {SessionId}: operating in memory-only mode", sessionId);
            return Task.CompletedTask;
        }

        if (userId != null)
        {
            _logger.LogDebug("SetAsync: userId resolved to {UserId} for session {SessionId}, initiating file write", userId, sessionId);
            _ = Task.Run(async () => await WriteToFileAsync(sessionId, userId, macroSource, macroCount));
        }
        else
        {
            _logger.LogWarning(
                "Skipping file write for session {SessionId}: userId could not be resolved. Macro programs will remain in memory only.",
                sessionId);
        }

        return Task.CompletedTask;
    }

    public async Task MergeAsync(string sessionId, IReadOnlyDictionary<string, string> macros)
    {
        if (macros.Count == 0)
        {
            _logger.LogDebug("MergeAsync called for session {SessionId} with no macro programs", sessionId);
            return;
        }

        var existingSource = await GetAsync(sessionId);
        var merged = LogParserService.ParseMacroDefinitionsFromSourceText(existingSource);

        foreach (var (name, source) in macros)
        {
            merged[name] = source;
        }

        _logger.LogDebug(
            "MergeAsync called for session {SessionId}: merging {IncomingCount} macro programs into {MergedCount} total definitions",
            sessionId, macros.Count, merged.Count);

        await SetAsync(sessionId, merged);
    }

    internal void RegisterSession(string sessionId, string userId)
    {
        if (string.IsNullOrWhiteSpace(sessionId))
            throw new ArgumentException("Session ID cannot be null or empty", nameof(sessionId));

        if (string.IsNullOrWhiteSpace(userId))
            throw new ArgumentException("User ID cannot be null or empty", nameof(userId));

        _sessionToUser.TryAdd(sessionId, userId);
        _logger.LogDebug("Registered session {SessionId} for user {UserId}", sessionId, userId);
    }

    internal string FormatMacroSource(string sessionId, string? userId, IReadOnlyDictionary<string, string> macros)
    {
        var sb = new StringBuilder();
        sb.AppendLine("/*");
        sb.AppendLine($" * Auto-generated macro program storage for session {sessionId}");
        if (!string.IsNullOrWhiteSpace(userId))
            sb.AppendLine($" * User: {userId}");
        sb.AppendLine($" * Last Updated: {DateTime.UtcNow:O}");
        sb.AppendLine(" *");
        sb.AppendLine(" * DO NOT EDIT MANUALLY - This file is automatically maintained by SAS Job Runner");
        sb.AppendLine(" */");
        sb.AppendLine();

        var validMacros = macros
            .Where(kvp => ValidateMacroSyntax(kvp.Key, kvp.Value, sessionId, out _))
            .OrderBy(kvp => kvp.Key, StringComparer.OrdinalIgnoreCase)
            .ToList();

        for (var i = 0; i < validMacros.Count; i++)
        {
            sb.Append(validMacros[i].Value.TrimEnd());
            if (i < validMacros.Count - 1)
            {
                sb.AppendLine();
                sb.AppendLine();
            }
        }

        return sb.ToString();
    }

    internal bool ValidateMacroSyntax(string macroName, string macroSource, string sessionId, out string? error)
    {
        error = null;

        if (string.IsNullOrWhiteSpace(macroName))
        {
            error = "Macro name is empty";
            _logger.LogWarning("Invalid macro for session {SessionId}: {ValidationError}", sessionId, error);
            return false;
        }

        if (!MacroNamePattern.IsMatch(macroName))
        {
            error = $"Macro name '{macroName}' is not a valid SAS identifier";
            _logger.LogWarning(
                "Invalid macro '{MacroName}' for session {SessionId}: {ValidationError}",
                macroName, sessionId, error);
            return false;
        }

        if (macroName.StartsWith("SYS_", StringComparison.OrdinalIgnoreCase))
        {
            error = $"Macro name '{macroName}' is a system macro and cannot be persisted";
            _logger.LogWarning(
                "Invalid macro '{MacroName}' for session {SessionId}: {ValidationError}",
                macroName, sessionId, error);
            return false;
        }

        if (string.IsNullOrWhiteSpace(macroSource))
        {
            error = "Macro source is empty";
            _logger.LogWarning(
                "Invalid macro '{MacroName}' for session {SessionId}: {ValidationError}",
                macroName, sessionId, error);
            return false;
        }

        var macroPattern = new Regex(
            $@"%macro\s+{Regex.Escape(macroName)}\b",
            RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);
        var mendPattern = new Regex(
            $@"%mend(?:\s+{Regex.Escape(macroName)})?\s*;",
            RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);

        var macroMatches = macroPattern.Matches(macroSource);
        var mendMatches = mendPattern.Matches(macroSource);

        if (macroMatches.Count != 1 || mendMatches.Count != 1)
        {
            error = $"Macro '{macroName}' must contain exactly one %macro and one matching %mend statement";
            _logger.LogWarning(
                "Invalid macro '{MacroName}' for session {SessionId}: {ValidationError}",
                macroName, sessionId, error);
            return false;
        }

        if (!AreQuotesBalanced(macroSource))
        {
            error = "Macro source contains unbalanced quotes";
            _logger.LogWarning(
                "Invalid macro '{MacroName}' for session {SessionId}: {ValidationError}",
                macroName, sessionId, error);
            return false;
        }

        if (!AreParenthesesBalanced(macroSource))
        {
            error = "Macro source contains unbalanced parentheses";
            _logger.LogWarning(
                "Invalid macro '{MacroName}' for session {SessionId}: {ValidationError}",
                macroName, sessionId, error);
            return false;
        }

        return true;
    }

    private static bool AreQuotesBalanced(string source)
    {
        var inSingle = false;
        var inDouble = false;
        var inBacktick = false;

        for (var i = 0; i < source.Length; i++)
        {
            var c = source[i];

            if (inSingle)
            {
                if (c == '\'' && (i + 1 >= source.Length || source[i + 1] != '\''))
                    inSingle = false;
                else if (c == '\'' && i + 1 < source.Length && source[i + 1] == '\'')
                    i++;
                continue;
            }

            if (inDouble)
            {
                if (c == '"' && (i + 1 >= source.Length || source[i + 1] != '"'))
                    inDouble = false;
                else if (c == '"' && i + 1 < source.Length && source[i + 1] == '"')
                    i++;
                continue;
            }

            if (inBacktick)
            {
                if (c == '`')
                    inBacktick = false;
                continue;
            }

            switch (c)
            {
                case '\'': inSingle = true; break;
                case '"': inDouble = true; break;
                case '`': inBacktick = true; break;
            }
        }

        return !inSingle && !inDouble && !inBacktick;
    }

    private static bool AreParenthesesBalanced(string source)
    {
        var depth = 0;
        var inSingle = false;
        var inDouble = false;
        var inBacktick = false;

        for (var i = 0; i < source.Length; i++)
        {
            var c = source[i];

            if (inSingle)
            {
                if (c == '\'' && (i + 1 >= source.Length || source[i + 1] != '\''))
                    inSingle = false;
                else if (c == '\'' && i + 1 < source.Length && source[i + 1] == '\'')
                    i++;
                continue;
            }

            if (inDouble)
            {
                if (c == '"' && (i + 1 >= source.Length || source[i + 1] != '"'))
                    inDouble = false;
                else if (c == '"' && i + 1 < source.Length && source[i + 1] == '"')
                    i++;
                continue;
            }

            if (inBacktick)
            {
                if (c == '`')
                    inBacktick = false;
                continue;
            }

            switch (c)
            {
                case '\'': inSingle = true; break;
                case '"': inDouble = true; break;
                case '`': inBacktick = true; break;
                case '(': depth++; break;
                case ')':
                    depth--;
                    if (depth < 0) return false;
                    break;
            }
        }

        return depth == 0;
    }

    private static int CountMacroDefinitions(string macroSource)
    {
        if (string.IsNullOrWhiteSpace(macroSource))
            return 0;

        return Regex.Matches(macroSource, @"%macro\s+\w+", RegexOptions.IgnoreCase).Count;
    }

    private string? TryResolveUserId(string sessionId)
    {
        if (_sessionToUser.TryGetValue(sessionId, out var cachedUserId))
            return cachedUserId;

        if (_memoryOnly)
        {
            _logger.LogDebug("Cannot resolve userId for session {SessionId}: StudyFolder is not configured", sessionId);
            return null;
        }

        try
        {
            var sessionsPath = Path.Combine(_studyFolder!, "sessions");

            if (!Directory.Exists(sessionsPath))
            {
                _logger.LogDebug("Sessions directory does not exist: {SessionsPath}", sessionsPath);
                return null;
            }

            foreach (var userDirectory in Directory.GetDirectories(sessionsPath))
            {
                var userId = Path.GetFileName(userDirectory);
                var sessionPath = Path.Combine(userDirectory, sessionId);

                if (Directory.Exists(sessionPath))
                {
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

    private SemaphoreSlim GetFileLock(string sessionId) =>
        _fileLocks.GetOrAdd(sessionId, _ => new SemaphoreSlim(1, 1));

    private string GetMacrosFilePath(string sessionId, string userId)
    {
        if (string.IsNullOrEmpty(_studyFolder))
            throw new InvalidOperationException("StudyFolder configuration is required for persistence");

        return Path.Combine(_studyFolder, "sessions", userId, sessionId, "macros.sas");
    }

    private async Task<string> LoadFromFileAsync(string sessionId)
    {
        if (_memoryOnly)
        {
            _logger.LogDebug("Cannot load macros for session {SessionId}: operating in memory-only mode", sessionId);
            return string.Empty;
        }

        var userId = TryResolveUserId(sessionId);

        if (userId == null)
        {
            _logger.LogDebug("Cannot load macros for session {SessionId}: userId could not be resolved", sessionId);
            return string.Empty;
        }

        string filePath;
        try
        {
            filePath = GetMacrosFilePath(sessionId, userId);
        }
        catch (InvalidOperationException ex)
        {
            _logger.LogWarning(ex, "Cannot load macros for session {SessionId}: {Message}", sessionId, ex.Message);
            return string.Empty;
        }

        if (!File.Exists(filePath))
        {
            _logger.LogDebug(
                "Macros file does not exist for session {SessionId} (user {UserId}) at path {FilePath}. This is expected for new sessions.",
                sessionId, userId, filePath);
            return string.Empty;
        }

        var fileLock = GetFileLock(sessionId);

        await fileLock.WaitAsync();
        try
        {
            var macroSource = await File.ReadAllTextAsync(filePath);
            var macroCount = CountMacroDefinitions(macroSource);

            _logger.LogInformation(
                "Successfully loaded {MacroCount} macro programs for session {SessionId} (user {UserId}) from {FilePath}",
                macroCount, sessionId, userId, filePath);

            return macroSource;
        }
        catch (FileNotFoundException ex)
        {
            _logger.LogDebug(ex,
                "Macros file not found for session {SessionId} (user {UserId}) at {FilePath}. This is expected for new sessions.",
                sessionId, userId, filePath);
            return string.Empty;
        }
        catch (UnauthorizedAccessException ex)
        {
            _logger.LogWarning(ex,
                "Access denied when reading macros file for session {SessionId} (user {UserId}) at {FilePath}. Check file permissions.",
                sessionId, userId, filePath);
            return _cache.TryGetValue(sessionId, out var cached) ? cached : string.Empty;
        }
        catch (IOException ex)
        {
            _logger.LogWarning(ex,
                "I/O error when reading macros file for session {SessionId} (user {UserId}) at {FilePath}: {Message}",
                sessionId, userId, filePath, ex.Message);
            return _cache.TryGetValue(sessionId, out var cached) ? cached : string.Empty;
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex,
                "Unexpected error when loading macros for session {SessionId} (user {UserId}) from {FilePath}: {Message}",
                sessionId, userId, filePath, ex.Message);
            return _cache.TryGetValue(sessionId, out var cached) ? cached : string.Empty;
        }
        finally
        {
            fileLock.Release();
        }
    }

    private async Task WriteToFileAsync(string sessionId, string userId, string macroSource, int macroCount)
    {
        _logger.LogDebug("WriteToFileAsync called for session {SessionId}, user {UserId}", sessionId, userId);

        if (_memoryOnly)
        {
            _logger.LogDebug("Skipping file write for session {SessionId}: operating in memory-only mode", sessionId);
            return;
        }

        var fileLock = GetFileLock(sessionId);

        await fileLock.WaitAsync();
        string? tempFilePath = null;
        try
        {
            var filePath = GetMacrosFilePath(sessionId, userId);
            var sessionDirectory = Path.GetDirectoryName(filePath)!;

            if (!Directory.Exists(sessionDirectory))
            {
                Directory.CreateDirectory(sessionDirectory);
                _logger.LogDebug("Created session directory: {SessionDirectory}", sessionDirectory);
            }

            tempFilePath = Path.Combine(sessionDirectory, ".macros.sas.tmp");
            await File.WriteAllTextAsync(tempFilePath, macroSource);
            File.Move(tempFilePath, filePath, overwrite: true);
            tempFilePath = null;

            _logger.LogInformation(
                "Successfully wrote {MacroCount} macro programs for session {SessionId} (user {UserId}) to {FilePath}",
                macroCount, sessionId, userId, filePath);
        }
        catch (IOException ex)
        {
            _logger.LogWarning(ex,
                "I/O error when writing macros file for session {SessionId} (user {UserId}) at {FilePath}: {Message}. Operation continues with in-memory cache.",
                sessionId, userId, GetMacrosFilePath(sessionId, userId), ex.Message);
        }
        catch (UnauthorizedAccessException ex)
        {
            _logger.LogWarning(ex,
                "Access denied when writing macros file for session {SessionId} (user {UserId}) at {FilePath}. Check file permissions. Operation continues with in-memory cache.",
                sessionId, userId, GetMacrosFilePath(sessionId, userId));
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex,
                "Unexpected error when writing macros file for session {SessionId} (user {UserId}) at {FilePath}: {Message}. Operation continues with in-memory cache.",
                sessionId, userId, GetMacrosFilePath(sessionId, userId), ex.Message);
        }
        finally
        {
            if (tempFilePath != null && File.Exists(tempFilePath))
            {
                try { File.Delete(tempFilePath); }
                catch (Exception ex)
                {
                    _logger.LogDebug(ex, "Failed to delete temporary macros file {TempFilePath}", tempFilePath);
                }
            }

            fileLock.Release();
        }
    }
}
