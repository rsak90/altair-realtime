using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;
using SasJobRunner.Services;
using Xunit;

namespace SasJobRunner.Tests;

public class MacroVarStoreRegisterSessionTests : IDisposable
{
    private readonly string _tempStudyFolder;
    private readonly MacroVarStore _store;

    public MacroVarStoreRegisterSessionTests()
    {
        // Create a temporary study folder for testing
        _tempStudyFolder = Path.Combine(Path.GetTempPath(), "MacroVarStoreRegisterSessionTests_" + Guid.NewGuid().ToString());
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

    [Fact]
    public void RegisterSession_WithValidParameters_StoresMapping()
    {
        // Arrange
        var sessionId = "test-session-123";
        var userId = "test-user-456";

        // Act
        _store.RegisterSession(sessionId, userId);

        // Assert - Verify by calling SetAsync which should use the registered userId
        var variables = new Dictionary<string, string> { ["TEST_VAR"] = "test_value" };
        var setTask = _store.SetAsync(sessionId, variables);
        
        // Wait for async file write to complete
        Task.Delay(500).Wait();

        // Verify file was written to correct path
        var expectedPath = Path.Combine(_tempStudyFolder, "sessions", userId, sessionId, "variables.json");
        Assert.True(File.Exists(expectedPath), $"Variables file should exist at {expectedPath}");
    }

    [Fact]
    public void RegisterSession_WithNullSessionId_ThrowsArgumentException()
    {
        // Arrange
        string? sessionId = null;
        var userId = "test-user";

        // Act & Assert
        var exception = Assert.Throws<ArgumentException>(() => _store.RegisterSession(sessionId!, userId));
        Assert.Contains("Session ID cannot be null or empty", exception.Message);
    }

    [Fact]
    public void RegisterSession_WithEmptySessionId_ThrowsArgumentException()
    {
        // Arrange
        var sessionId = "";
        var userId = "test-user";

        // Act & Assert
        var exception = Assert.Throws<ArgumentException>(() => _store.RegisterSession(sessionId, userId));
        Assert.Contains("Session ID cannot be null or empty", exception.Message);
    }

    [Fact]
    public void RegisterSession_WithNullUserId_ThrowsArgumentException()
    {
        // Arrange
        var sessionId = "test-session";
        string? userId = null;

        // Act & Assert
        var exception = Assert.Throws<ArgumentException>(() => _store.RegisterSession(sessionId, userId!));
        Assert.Contains("User ID cannot be null or empty", exception.Message);
    }

    [Fact]
    public void RegisterSession_WithEmptyUserId_ThrowsArgumentException()
    {
        // Arrange
        var sessionId = "test-session";
        var userId = "";

        // Act & Assert
        var exception = Assert.Throws<ArgumentException>(() => _store.RegisterSession(sessionId, userId));
        Assert.Contains("User ID cannot be null or empty", exception.Message);
    }

    [Fact]
    public void RegisterSession_BeforeGetAsync_PreventsFilesystemScan()
    {
        // Arrange
        var sessionId = "early-registered-session";
        var userId = "test-user";

        // Act - Register session before any GetAsync call
        _store.RegisterSession(sessionId, userId);

        // Now call SetAsync to write the file
        var variables = new Dictionary<string, string> { ["VAR1"] = "value1" };
        _store.SetAsync(sessionId, variables).Wait();
        
        // Wait for async file write
        Task.Delay(500).Wait();

        // Assert - Verify file was written to correct path without needing filesystem scan
        var expectedPath = Path.Combine(_tempStudyFolder, "sessions", userId, sessionId, "variables.json");
        Assert.True(File.Exists(expectedPath), $"Variables file should exist at {expectedPath}");
    }

    [Fact]
    public void RegisterSession_CalledMultipleTimes_IdempotentBehavior()
    {
        // Arrange
        var sessionId = "duplicate-session";
        var userId = "test-user";

        // Act - Register the same session multiple times
        _store.RegisterSession(sessionId, userId);
        _store.RegisterSession(sessionId, userId);
        _store.RegisterSession(sessionId, userId);

        // Assert - Should not throw and mapping should still work
        var variables = new Dictionary<string, string> { ["VAR"] = "value" };
        _store.SetAsync(sessionId, variables).Wait();
        
        Task.Delay(500).Wait();
        
        var expectedPath = Path.Combine(_tempStudyFolder, "sessions", userId, sessionId, "variables.json");
        Assert.True(File.Exists(expectedPath));
    }

    [Fact]
    public async Task RegisterSession_IntegrationWithGetAsync_WorksCorrectly()
    {
        // Arrange
        var sessionId = "integration-session";
        var userId = "integration-user";
        var variables = new Dictionary<string, string>
        {
            ["INTEGRATION_VAR"] = "integration_value"
        };

        // Act - Register session, set variables, then retrieve them
        _store.RegisterSession(sessionId, userId);
        await _store.SetAsync(sessionId, variables);
        
        // Wait for file write
        await Task.Delay(500);
        
        // Clear the in-memory cache by creating a new store instance
        var configuration = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?> { ["SessionStorage:StudyFolder"] = _tempStudyFolder })
            .Build();
        var loggerFactory = LoggerFactory.Create(builder => builder.AddConsole().SetMinimumLevel(LogLevel.Debug));
        var logger = loggerFactory.CreateLogger<MacroVarStore>();
        var newStore = new MacroVarStore(configuration, logger);
        
        // Retrieve from new store (should load from disk)
        var result = await newStore.GetAsync(sessionId);

        // Assert
        Assert.NotNull(result);
        Assert.Single(result);
        Assert.Equal("integration_value", result["INTEGRATION_VAR"]);
    }
}
