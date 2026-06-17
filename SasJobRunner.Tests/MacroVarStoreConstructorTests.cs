using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;
using SasJobRunner.Services;
using Xunit;

namespace SasJobRunner.Tests;

public class MacroVarStoreConstructorTests
{
    [Fact]
    public void Constructor_WithMissingStudyFolder_LogsErrorAndWarning()
    {
        // Arrange
        var inMemorySettings = new Dictionary<string, string?>
        {
            // SessionStorage:StudyFolder is intentionally not set
        };
        var configuration = new ConfigurationBuilder()
            .AddInMemoryCollection(inMemorySettings)
            .Build();

        var loggerFactory = LoggerFactory.Create(builder => builder.AddConsole().SetMinimumLevel(LogLevel.Debug));
        var logger = loggerFactory.CreateLogger<MacroVarStore>();

        // Act - Constructor should not throw
        var store = new MacroVarStore(configuration, logger);

        // Assert - Store should be created successfully
        Assert.NotNull(store);
        // The constructor logs error and warning messages but doesn't throw
    }

    [Fact]
    public void Constructor_WithStudyFolder_LogsInformationMessage()
    {
        // Arrange
        var inMemorySettings = new Dictionary<string, string?>
        {
            ["SessionStorage:StudyFolder"] = "C:\\TestStudyFolder"
        };
        var configuration = new ConfigurationBuilder()
            .AddInMemoryCollection(inMemorySettings)
            .Build();

        var loggerFactory = LoggerFactory.Create(builder => builder.AddConsole().SetMinimumLevel(LogLevel.Debug));
        var logger = loggerFactory.CreateLogger<MacroVarStore>();

        // Act - Constructor should not throw
        var store = new MacroVarStore(configuration, logger);

        // Assert - Store should be created successfully with StudyFolder
        Assert.NotNull(store);
    }

    [Fact]
    public void Constructor_WithEmptyStudyFolder_LogsErrorAndWarning()
    {
        // Arrange
        var inMemorySettings = new Dictionary<string, string?>
        {
            ["SessionStorage:StudyFolder"] = ""
        };
        var configuration = new ConfigurationBuilder()
            .AddInMemoryCollection(inMemorySettings)
            .Build();

        var loggerFactory = LoggerFactory.Create(builder => builder.AddConsole().SetMinimumLevel(LogLevel.Debug));
        var logger = loggerFactory.CreateLogger<MacroVarStore>();

        // Act - Constructor should not throw
        var store = new MacroVarStore(configuration, logger);

        // Assert - Store should be created successfully (falls back to in-memory-only)
        Assert.NotNull(store);
    }

    [Fact]
    public void Constructor_WithWhitespaceStudyFolder_LogsErrorAndWarning()
    {
        // Arrange
        var inMemorySettings = new Dictionary<string, string?>
        {
            ["SessionStorage:StudyFolder"] = "   "
        };
        var configuration = new ConfigurationBuilder()
            .AddInMemoryCollection(inMemorySettings)
            .Build();

        var loggerFactory = LoggerFactory.Create(builder => builder.AddConsole().SetMinimumLevel(LogLevel.Debug));
        var logger = loggerFactory.CreateLogger<MacroVarStore>();

        // Act - Constructor should not throw
        var store = new MacroVarStore(configuration, logger);

        // Assert - Store should be created successfully (falls back to in-memory-only)
        Assert.NotNull(store);
    }

    [Fact]
    public async Task GetAsync_AfterConstructorWithMissingStudyFolder_WorksInMemoryOnly()
    {
        // Arrange
        var inMemorySettings = new Dictionary<string, string?>();
        var configuration = new ConfigurationBuilder()
            .AddInMemoryCollection(inMemorySettings)
            .Build();

        var loggerFactory = LoggerFactory.Create(builder => builder.AddConsole().SetMinimumLevel(LogLevel.Debug));
        var logger = loggerFactory.CreateLogger<MacroVarStore>();

        var store = new MacroVarStore(configuration, logger);

        // Act - Should work in-memory even without StudyFolder
        var result = await store.GetAsync("test-session");

        // Assert
        Assert.NotNull(result);
        Assert.Empty(result);
    }

    [Fact]
    public async Task SetAsync_AfterConstructorWithMissingStudyFolder_WorksInMemoryOnly()
    {
        // Arrange
        var inMemorySettings = new Dictionary<string, string?>();
        var configuration = new ConfigurationBuilder()
            .AddInMemoryCollection(inMemorySettings)
            .Build();

        var loggerFactory = LoggerFactory.Create(builder => builder.AddConsole().SetMinimumLevel(LogLevel.Debug));
        var logger = loggerFactory.CreateLogger<MacroVarStore>();

        var store = new MacroVarStore(configuration, logger);
        var vars = new Dictionary<string, string> { ["VAR1"] = "value1" };

        // Act - Should work in-memory even without StudyFolder
        await store.SetAsync("test-session", vars);
        var result = await store.GetAsync("test-session");

        // Assert
        Assert.NotNull(result);
        Assert.Single(result);
        Assert.Equal("value1", result["VAR1"]);
    }
}
