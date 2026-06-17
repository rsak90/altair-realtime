using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;
using SasJobRunner.Services;
using Xunit;

namespace SasJobRunner.Tests;

public class MacroVarStoreGetAsyncTests : IDisposable
{
    private readonly string _tempStudyFolder;
    private readonly MacroVarStore _store;

    public MacroVarStoreGetAsyncTests()
    {
        // Create a temporary study folder for testing
        _tempStudyFolder = Path.Combine(Path.GetTempPath(), "MacroVarStoreTests_" + Guid.NewGuid().ToString());
        Directory.CreateDirectory(_tempStudyFolder);

        // Configure the store with the temp study folder
        var inMemorySettings = new Dictionary<string, string?>
        {
            ["SessionStorage:StudyFolder"] = _tempStudyFolder
        };
        var configuration = new ConfigurationBuilder()
            .AddInMemoryCollection(inMemorySettings)
            .Build();

        var loggerFactory = LoggerFactory.Create(builder => builder.AddConsole().SetMinimumLevel(LogLevel.Debug));
        var logger = loggerFactory.CreateLogger<MacroVarStore>();

        _store = new MacroVarStore(configuration, logger);
    }

    public void Dispose()
    {
        // Clean up the temporary study folder
        if (Directory.Exists(_tempStudyFolder))
        {
            Directory.Delete(_tempStudyFolder, recursive: true);
        }
    }

    /// <summary>
    /// Helper method to create a variables.json file with the expected structure
    /// </summary>
    private async Task CreateVariablesFile(string sessionId, string userId, Dictionary<string, string> variables)
    {
        var sessionFolder = Path.Combine(_tempStudyFolder, "sessions", userId, sessionId);
        Directory.CreateDirectory(sessionFolder);

        var json = $$"""
        {
          "metadata": {
            "userId": "{{userId}}",
            "lastUpdated": "{{DateTime.UtcNow:O}}"
          },
          "variables": {
            {{string.Join(",\n    ", variables.Select(kvp => $"\"{kvp.Key}\": \"{kvp.Value}\""))}}
          }
        }
        """;

        var filePath = Path.Combine(sessionFolder, "variables.json");
        await File.WriteAllTextAsync(filePath, json);
    }

    [Fact]
    public async Task GetAsync_WithCachedSession_ReturnsFromCacheImmediately()
    {
        // Arrange - populate cache via SetAsync
        var sessionId = "cached-session";
        var expectedVars = new Dictionary<string, string>
        {
            ["VAR1"] = "value1",
            ["VAR2"] = "value2"
        };
        await _store.SetAsync(sessionId, expectedVars);

        // Act - Get from cache
        var result = await _store.GetAsync(sessionId);

        // Assert
        Assert.NotNull(result);
        Assert.Equal(2, result.Count);
        Assert.Equal("value1", result["VAR1"]);
        Assert.Equal("value2", result["VAR2"]);
    }

    [Fact]
    public async Task GetAsync_WithNonExistentSession_ReturnsEmptyAndMarksLoaded()
    {
        // Arrange - session that doesn't exist in cache or on disk
        var sessionId = "non-existent-session";

        // Act - First call should attempt to load from disk
        var result1 = await _store.GetAsync(sessionId);

        // Act - Second call should return empty without disk access
        var result2 = await _store.GetAsync(sessionId);

        // Assert
        Assert.NotNull(result1);
        Assert.Empty(result1);
        Assert.NotNull(result2);
        Assert.Empty(result2);
    }

    [Fact]
    public async Task GetAsync_WithPersistedSession_LoadsFromDiskAndCaches()
    {
        // Arrange - manually create a variables.json file
        var sessionId = "persisted-session";
        var userId = "test-user";
        var variables = new Dictionary<string, string>
        {
            ["LOADED_VAR1"] = "loaded_value1",
            ["LOADED_VAR2"] = "loaded_value2"
        };
        await CreateVariablesFile(sessionId, userId, variables);

        // Act - First GetAsync should load from disk
        var result = await _store.GetAsync(sessionId);

        // Assert
        Assert.NotNull(result);
        Assert.Equal(2, result.Count);
        Assert.Equal("loaded_value1", result["LOADED_VAR1"]);
        Assert.Equal("loaded_value2", result["LOADED_VAR2"]);
    }

    [Fact]
    public async Task GetAsync_LoadedFromDisk_SubsequentCallsUseCacheNotDisk()
    {
        // Arrange - manually create a variables.json file
        var sessionId = "lazy-load-session";
        var userId = "test-user";
        var variables = new Dictionary<string, string>
        {
            ["VAR1"] = "original_value"
        };
        await CreateVariablesFile(sessionId, userId, variables);

        // Act - First call loads from disk
        var result1 = await _store.GetAsync(sessionId);

        // Modify the file on disk (simulating external change)
        var modifiedVariables = new Dictionary<string, string>
        {
            ["VAR1"] = "modified_value"
        };
        await CreateVariablesFile(sessionId, userId, modifiedVariables);

        // Act - Second call should use cache, not re-read from disk
        var result2 = await _store.GetAsync(sessionId);

        // Assert - result2 should still have original_value (from cache)
        Assert.NotNull(result1);
        Assert.Equal("original_value", result1["VAR1"]);
        Assert.NotNull(result2);
        Assert.Equal("original_value", result2["VAR1"]); // Should be from cache, not modified_value
    }

    [Fact]
    public async Task GetAsync_EmptyPersistedSession_LoadsEmptyAndCaches()
    {
        // Arrange - create a variables.json file with no variables
        var sessionId = "empty-session";
        var userId = "test-user";
        var variables = new Dictionary<string, string>(); // Empty
        await CreateVariablesFile(sessionId, userId, variables);

        // Act
        var result = await _store.GetAsync(sessionId);

        // Assert
        Assert.NotNull(result);
        Assert.Empty(result);
    }

    [Fact]
    public async Task GetAsync_CacheFirstBehavior_BypassesDiskWhenCached()
    {
        // Arrange - set variables in cache first
        var sessionId = "cache-first-session";
        var cachedVars = new Dictionary<string, string>
        {
            ["CACHED"] = "cached_value"
        };
        await _store.SetAsync(sessionId, cachedVars);

        // Now create a different file on disk
        var userId = "test-user";
        var diskVars = new Dictionary<string, string>
        {
            ["DISK"] = "disk_value"
        };
        await CreateVariablesFile(sessionId, userId, diskVars);

        // Act - GetAsync should return cached value, not disk value
        var result = await _store.GetAsync(sessionId);

        // Assert - should have cached value, not disk value
        Assert.NotNull(result);
        Assert.Single(result);
        Assert.True(result.ContainsKey("CACHED"));
        Assert.Equal("cached_value", result["CACHED"]);
        Assert.False(result.ContainsKey("DISK")); // Disk value should not be loaded
    }
}
