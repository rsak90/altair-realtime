using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;
using SasJobRunner.Services;
using System.Text.Json;
using SasJobRunner.Models;

namespace SasJobRunner.Tests;

public class MacroVarStoreSetVarAsyncTests : IDisposable
{
    private readonly string _tempStudyFolder;
    private readonly MacroVarStore _store;

    public MacroVarStoreSetVarAsyncTests()
    {
        // Create a temporary study folder for testing
        _tempStudyFolder = Path.Combine(Path.GetTempPath(), "MacroVarStoreSetVarAsyncTests_" + Guid.NewGuid().ToString());
        Directory.CreateDirectory(_tempStudyFolder);

        // Create test session directory structure
        var sessionPath = Path.Combine(_tempStudyFolder, "sessions", "test-user", "test-session");
        Directory.CreateDirectory(sessionPath);

        // Setup configuration
        var configData = new Dictionary<string, string?>
        {
            ["SessionStorage:StudyFolder"] = _tempStudyFolder
        };
        var configuration = new ConfigurationBuilder()
            .AddInMemoryCollection(configData)
            .Build();

        var loggerFactory = LoggerFactory.Create(builder => builder.AddConsole().SetMinimumLevel(LogLevel.Debug));
        var logger = loggerFactory.CreateLogger<MacroVarStore>();

        _store = new MacroVarStore(configuration, logger);
    }

    public void Dispose()
    {
        // Clean up temporary folder
        if (Directory.Exists(_tempStudyFolder))
        {
            Directory.Delete(_tempStudyFolder, true);
        }
    }

    [Fact]
    public async Task SetVarAsync_UpdatesCacheImmediately()
    {
        // Arrange
        var sessionId = "test-session";
        var varName = "TEST_VAR";
        var varValue = "test-value";

        // Act
        await _store.SetVarAsync(sessionId, varName, varValue);

        // Assert - Cache should be updated immediately
        var result = await _store.GetAsync(sessionId);
        Assert.NotNull(result);
        Assert.True(result.ContainsKey(varName));
        Assert.Equal(varValue, result[varName]);
    }

    [Fact]
    public async Task SetVarAsync_TriggersFileWrite()
    {
        // Arrange
        var sessionId = "test-session";
        var varName = "PERSIST_VAR";
        var varValue = "persist-value";
        var expectedFilePath = Path.Combine(_tempStudyFolder, "sessions", "test-user", sessionId, "variables.json");

        // Act
        await _store.SetVarAsync(sessionId, varName, varValue);

        // Wait a bit for the fire-and-forget task to complete
        await Task.Delay(500);

        // Assert - File should be written
        Assert.True(File.Exists(expectedFilePath), $"Variables file should exist at {expectedFilePath}");

        // Verify file content
        var jsonContent = await File.ReadAllTextAsync(expectedFilePath);
        var macroVarFile = JsonSerializer.Deserialize<MacroVarFile>(jsonContent);
        
        Assert.NotNull(macroVarFile);
        Assert.NotNull(macroVarFile.Variables);
        Assert.True(macroVarFile.Variables.ContainsKey(varName));
        Assert.Equal(varValue, macroVarFile.Variables[varName]);
        Assert.Equal("test-user", macroVarFile.Metadata.UserId);
    }

    [Fact]
    public async Task SetVarAsync_UpdatesExistingVariable()
    {
        // Arrange
        var sessionId = "test-session";
        var varName = "UPDATE_VAR";
        var initialValue = "initial";
        var updatedValue = "updated";
        var expectedFilePath = Path.Combine(_tempStudyFolder, "sessions", "test-user", sessionId, "variables.json");

        // Act - Set initial value
        await _store.SetVarAsync(sessionId, varName, initialValue);
        await Task.Delay(300);

        // Act - Update value
        await _store.SetVarAsync(sessionId, varName, updatedValue);
        await Task.Delay(300);

        // Assert - Cache should reflect updated value
        var result = await _store.GetAsync(sessionId);
        Assert.Equal(updatedValue, result[varName]);

        // Assert - File should reflect updated value
        Assert.True(File.Exists(expectedFilePath));
        var jsonContent = await File.ReadAllTextAsync(expectedFilePath);
        var macroVarFile = JsonSerializer.Deserialize<MacroVarFile>(jsonContent);
        
        Assert.NotNull(macroVarFile);
        Assert.Equal(updatedValue, macroVarFile.Variables[varName]);
    }

    [Fact]
    public async Task SetVarAsync_AddsToExistingVariables()
    {
        // Arrange
        var sessionId = "test-session";
        var var1Name = "VAR1";
        var var1Value = "value1";
        var var2Name = "VAR2";
        var var2Value = "value2";
        var expectedFilePath = Path.Combine(_tempStudyFolder, "sessions", "test-user", sessionId, "variables.json");

        // Act - Add first variable
        await _store.SetVarAsync(sessionId, var1Name, var1Value);
        await Task.Delay(300);

        // Act - Add second variable
        await _store.SetVarAsync(sessionId, var2Name, var2Value);
        await Task.Delay(300);

        // Assert - Cache should contain both variables
        var result = await _store.GetAsync(sessionId);
        Assert.Equal(2, result.Count);
        Assert.Equal(var1Value, result[var1Name]);
        Assert.Equal(var2Value, result[var2Name]);

        // Assert - File should contain both variables
        Assert.True(File.Exists(expectedFilePath));
        var jsonContent = await File.ReadAllTextAsync(expectedFilePath);
        var macroVarFile = JsonSerializer.Deserialize<MacroVarFile>(jsonContent);
        
        Assert.NotNull(macroVarFile);
        Assert.Equal(2, macroVarFile.Variables.Count);
        Assert.Equal(var1Value, macroVarFile.Variables[var1Name]);
        Assert.Equal(var2Value, macroVarFile.Variables[var2Name]);
    }

    [Fact]
    public async Task SetVarAsync_ReturnsImmediately()
    {
        // Arrange
        var sessionId = "test-session";
        var varName = "IMMEDIATE_VAR";
        var varValue = "immediate-value";

        // Act - Should return immediately without waiting for file write
        var startTime = DateTime.UtcNow;
        await _store.SetVarAsync(sessionId, varName, varValue);
        var elapsed = DateTime.UtcNow - startTime;

        // Assert - Should complete in well under 100ms (file write happens in background)
        Assert.True(elapsed.TotalMilliseconds < 100, $"SetVarAsync took {elapsed.TotalMilliseconds}ms, expected < 100ms");
    }
}
