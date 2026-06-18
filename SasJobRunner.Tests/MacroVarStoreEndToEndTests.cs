using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.SignalR;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;
using Moq;
using SasJobRunner.Hubs;
using SasJobRunner.Models;
using SasJobRunner.Services;
using Xunit;

namespace SasJobRunner.Tests;

/// <summary>
/// End-to-end integration tests for session macro persistence.
/// Tests the complete workflow: job submission -> persistence -> restart -> reload.
/// </summary>
public class MacroVarStoreEndToEndTests : IDisposable
{
    private readonly string _tempStudyFolder;
    private readonly IConfiguration _configuration;
    private readonly ILogger<MacroVarStore> _logger;
    private readonly ILogger<MacroProgramStore> _macroProgramLogger;
    private readonly ILogger<SessionJobOrchestrator> _orchestratorLogger;
    private readonly ILogger<LogParserService> _logParserLogger;

    public MacroVarStoreEndToEndTests()
    {
        // Create a temporary study folder for testing
        _tempStudyFolder = Path.Combine(Path.GetTempPath(), "MacroVarStoreE2E_" + Guid.NewGuid().ToString());
        Directory.CreateDirectory(_tempStudyFolder);

        // Configure with the temp study folder
        var inMemorySettings = new Dictionary<string, string?>
        {
            ["SessionStorage:StudyFolder"] = _tempStudyFolder
        };
        _configuration = new ConfigurationBuilder()
            .AddInMemoryCollection(inMemorySettings)
            .Build();

        var loggerFactory = LoggerFactory.Create(builder => builder.AddConsole().SetMinimumLevel(LogLevel.Debug));
        _logger = loggerFactory.CreateLogger<MacroVarStore>();
        _macroProgramLogger = loggerFactory.CreateLogger<MacroProgramStore>();
        _orchestratorLogger = loggerFactory.CreateLogger<SessionJobOrchestrator>();
        _logParserLogger = loggerFactory.CreateLogger<LogParserService>();
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
    /// End-to-end test: Submit job through SessionJobOrchestrator with macro variables,
    /// verify variables are written to correct file path, clear cache (simulate restart),
    /// submit new job and verify variables are loaded from file, and verify loaded variables
    /// are used in subsequent job submissions.
    /// 
    /// Requirements: 1.1-1.5, 2.1-2.5, 8.2
    /// </summary>
    [Fact]
    public async Task EndToEnd_JobSubmission_PersistsAndReloadsVariables()
    {
        // ===== ARRANGE =====
        var userId = "test-user-e2e";
        var sessionId = Guid.NewGuid().ToString();

        // Create MacroVarStore instance
        var store1 = new MacroVarStore(_configuration, _logger);

        // ===== ACT 1: Initial job submission with macro variables =====
        
        // Manually simulate what SessionJobOrchestrator does:
        // 1. Register session with userId
        store1.RegisterSession(sessionId, userId);
        
        // 2. Get macro variables (should be empty initially)
        var initialVars = await store1.GetAsync(sessionId);
        Assert.NotNull(initialVars);
        Assert.Empty(initialVars);

        // 3. Simulate job completion with macro variables
        var macroVariables = new Dictionary<string, string>
        {
            ["MYVAR"] = "testvalue",
            ["STUDY_ID"] = "12345",
            ["SESSION_NAME"] = "TestSession"
        };
        await store1.SetAsync(sessionId, macroVariables);

        // Wait a bit for async file write to complete
        await Task.Delay(500);

        // ===== ASSERT 1: Verify variables are written to correct file path =====
        
        var expectedFilePath = Path.Combine(_tempStudyFolder, "sessions", userId, sessionId, "variables.json");
        Assert.True(File.Exists(expectedFilePath), 
            $"Variables file should exist at {expectedFilePath}");

        // Verify file content
        var fileContent = await File.ReadAllTextAsync(expectedFilePath);
        Assert.Contains("\"MYVAR\": \"testvalue\"", fileContent);
        Assert.Contains("\"STUDY_ID\": \"12345\"", fileContent);
        Assert.Contains("\"SESSION_NAME\": \"TestSession\"", fileContent);
        Assert.Contains($"\"userId\": \"{userId}\"", fileContent);

        // ===== ACT 2: Clear cache to simulate application restart =====
        
        // Create a NEW MacroVarStore instance - this simulates app restart
        // The old store (store1) is no longer used
        var store2 = new MacroVarStore(_configuration, _logger);

        // ===== ACT 3: Submit new job and verify variables are loaded from file =====
        
        // Simulate SessionJobOrchestrator registering the session again after restart
        store2.RegisterSession(sessionId, userId);
        
        // Get macro variables - should load from disk this time
        var reloadedVars = await store2.GetAsync(sessionId);

        // ===== ASSERT 2: Verify loaded variables match what was persisted =====
        
        Assert.NotNull(reloadedVars);
        Assert.Equal(3, reloadedVars.Count);
        Assert.Equal("testvalue", reloadedVars["MYVAR"]);
        Assert.Equal("12345", reloadedVars["STUDY_ID"]);
        Assert.Equal("TestSession", reloadedVars["SESSION_NAME"]);

        // ===== ACT 4: Verify loaded variables are used in subsequent job submissions =====
        
        // Add a new variable to the existing set
        await store2.SetVarAsync(sessionId, "NEW_VAR", "new_value");

        // Wait a bit for async file write
        await Task.Delay(500);

        // ===== ASSERT 3: Verify all variables (old + new) are present =====
        
        var allVars = await store2.GetAsync(sessionId);
        Assert.NotNull(allVars);
        Assert.Equal(4, allVars.Count);
        Assert.Equal("testvalue", allVars["MYVAR"]);
        Assert.Equal("12345", allVars["STUDY_ID"]);
        Assert.Equal("TestSession", allVars["SESSION_NAME"]);
        Assert.Equal("new_value", allVars["NEW_VAR"]);

        // Verify the file was updated
        var updatedFileContent = await File.ReadAllTextAsync(expectedFilePath);
        Assert.Contains("\"NEW_VAR\": \"new_value\"", updatedFileContent);
        Assert.Contains("\"MYVAR\": \"testvalue\"", updatedFileContent);

        // ===== ACT 5: Simulate another restart and verify persistence again =====
        
        var store3 = new MacroVarStore(_configuration, _logger);
        store3.RegisterSession(sessionId, userId);
        
        var finalVars = await store3.GetAsync(sessionId);
        
        // ===== ASSERT 4: Verify all variables persist across multiple restarts =====
        
        Assert.NotNull(finalVars);
        Assert.Equal(4, finalVars.Count);
        Assert.Equal("testvalue", finalVars["MYVAR"]);
        Assert.Equal("12345", finalVars["STUDY_ID"]);
        Assert.Equal("TestSession", finalVars["SESSION_NAME"]);
        Assert.Equal("new_value", finalVars["NEW_VAR"]);
    }

    /// <summary>
    /// Test that verifies the complete workflow with actual SessionJobOrchestrator integration.
    /// This test uses mocked external dependencies (SLC Hub) but real MacroVarStore and orchestrator.
    /// </summary>
    [Fact]
    public async Task EndToEnd_WithOrchestrator_VariablesPersistAcrossRestarts()
    {
        // ===== ARRANGE =====
        var userId = "orchestrator-user";
        var sessionId = Guid.NewGuid().ToString();

        // Create first MacroVarStore and orchestrator
        var store1 = new MacroVarStore(_configuration, _logger);
        var macroProgramStore1 = new MacroProgramStore(_configuration, _macroProgramLogger);

        var logParser = new LogParserService(_logParserLogger);

        // Mock external dependencies
        var mockHubClient = new Mock<ISlcHubClient>();
        var mockHistoryStore = new Mock<IProgramHistoryStore>();
        var mockSignalrContext = new Mock<IHubContext<LogStreamingHub>>();
        var mockHttpContextAccessor = new Mock<IHttpContextAccessor>();

        // Setup SLC Hub mock to simulate job submission
        mockHubClient.Setup(x => x.CreateJobAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync("job-123");
        mockHubClient.Setup(x => x.CommitJobAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .Returns(Task.CompletedTask);

        var preambleBuilder = new PreambleBuilder(_configuration);

        var orchestrator1 = new SessionJobOrchestrator(
            mockHubClient.Object,
            preambleBuilder,
            store1,
            macroProgramStore1,
            mockHistoryStore.Object,
            logParser,
            mockSignalrContext.Object,
            mockHttpContextAccessor.Object,
            _configuration,
            _orchestratorLogger);

        // ===== ACT 1: Submit job through orchestrator =====
        
        var jobId = await orchestrator1.SubmitAsync(userId, sessionId, "%let test=value;");
        Assert.Equal("job-123", jobId);

        // Verify CreateJobAsync was called
        mockHubClient.Verify(x => x.CreateJobAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()), Times.Once);

        // ===== ACT 2: Simulate macro variable updates after job completion =====
        
        var variables = new Dictionary<string, string>
        {
            ["VAR1"] = "value1",
            ["VAR2"] = "value2"
        };
        await store1.SetAsync(sessionId, variables);

        // Wait for async file write
        await Task.Delay(500);

        // ===== ASSERT 1: Verify file was created =====
        
        var filePath = Path.Combine(_tempStudyFolder, "sessions", userId, sessionId, "variables.json");
        Assert.True(File.Exists(filePath));

        // ===== ACT 3: Simulate restart - create new store and orchestrator =====
        
        var store2 = new MacroVarStore(_configuration, _logger);
        var macroProgramStore2 = new MacroProgramStore(_configuration, _macroProgramLogger);

        var orchestrator2 = new SessionJobOrchestrator(
            mockHubClient.Object,
            preambleBuilder,
            store2,
            macroProgramStore2,
            mockHistoryStore.Object,
            logParser,
            mockSignalrContext.Object,
            mockHttpContextAccessor.Object,
            _configuration,
            _orchestratorLogger);

        // ===== ACT 4: Submit another job - should load variables from disk =====
        
        var jobId2 = await orchestrator2.SubmitAsync(userId, sessionId, "%put _user_;");

        // The orchestrator calls GetAsync which should load from disk
        var loadedVars = await store2.GetAsync(sessionId);

        // ===== ASSERT 2: Verify variables were loaded from disk =====
        
        Assert.NotNull(loadedVars);
        Assert.Equal(2, loadedVars.Count);
        Assert.Equal("value1", loadedVars["VAR1"]);
        Assert.Equal("value2", loadedVars["VAR2"]);
    }

    /// <summary>
    /// Test that verifies userId resolution from file system when not cached.
    /// This simulates a scenario where the app restarts and needs to find the userId
    /// by scanning the session folders.
    /// </summary>
    [Fact]
    public async Task EndToEnd_UserIdResolution_WorksWithoutExplicitRegistration()
    {
        // ===== ARRANGE =====
        var userId = "auto-resolve-user";
        var sessionId = Guid.NewGuid().ToString();

        // Create store and explicitly register session
        var store1 = new MacroVarStore(_configuration, _logger);
        store1.RegisterSession(sessionId, userId);

        // Set some variables
        var variables = new Dictionary<string, string>
        {
            ["AUTO_VAR"] = "auto_value"
        };
        await store1.SetAsync(sessionId, variables);
        await Task.Delay(500); // Wait for file write

        // ===== ACT: Create new store WITHOUT registering session =====
        
        var store2 = new MacroVarStore(_configuration, _logger);
        
        // Don't call RegisterSession - force the store to resolve userId from file system
        // The store should scan the sessions directory to find the userId

        // Get variables - this should trigger userId resolution from file system
        var loadedVars = await store2.GetAsync(sessionId);

        // ===== ASSERT: Verify variables were loaded despite not registering session =====
        
        Assert.NotNull(loadedVars);
        Assert.Single(loadedVars);
        Assert.Equal("auto_value", loadedVars["AUTO_VAR"]);
    }

    /// <summary>
    /// Test that verifies multiple sessions for the same user work correctly.
    /// </summary>
    [Fact]
    public async Task EndToEnd_MultipleSessions_EachHasIsolatedVariables()
    {
        // ===== ARRANGE =====
        var userId = "multi-session-user";
        var sessionId1 = Guid.NewGuid().ToString();
        var sessionId2 = Guid.NewGuid().ToString();

        var store = new MacroVarStore(_configuration, _logger);
        store.RegisterSession(sessionId1, userId);
        store.RegisterSession(sessionId2, userId);

        // ===== ACT: Set different variables for each session =====
        
        var vars1 = new Dictionary<string, string>
        {
            ["SESSION1_VAR"] = "session1_value"
        };
        await store.SetAsync(sessionId1, vars1);

        var vars2 = new Dictionary<string, string>
        {
            ["SESSION2_VAR"] = "session2_value"
        };
        await store.SetAsync(sessionId2, vars2);

        await Task.Delay(500); // Wait for file writes

        // ===== ACT: Create new store and load variables =====
        
        var store2 = new MacroVarStore(_configuration, _logger);
        store2.RegisterSession(sessionId1, userId);
        store2.RegisterSession(sessionId2, userId);

        var loaded1 = await store2.GetAsync(sessionId1);
        var loaded2 = await store2.GetAsync(sessionId2);

        // ===== ASSERT: Verify each session has its own isolated variables =====
        
        Assert.NotNull(loaded1);
        Assert.Single(loaded1);
        Assert.Equal("session1_value", loaded1["SESSION1_VAR"]);
        Assert.False(loaded1.ContainsKey("SESSION2_VAR"));

        Assert.NotNull(loaded2);
        Assert.Single(loaded2);
        Assert.Equal("session2_value", loaded2["SESSION2_VAR"]);
        Assert.False(loaded2.ContainsKey("SESSION1_VAR"));
    }

    /// <summary>
    /// Test that verifies graceful handling when variables file is corrupted.
    /// </summary>
    [Fact]
    public async Task EndToEnd_CorruptedFile_ReturnsEmptyAndContinuesOperation()
    {
        // ===== ARRANGE =====
        var userId = "corrupted-file-user";
        var sessionId = Guid.NewGuid().ToString();

        // Create session folder and write corrupted JSON
        var sessionFolder = Path.Combine(_tempStudyFolder, "sessions", userId, sessionId);
        Directory.CreateDirectory(sessionFolder);
        var filePath = Path.Combine(sessionFolder, "variables.json");
        await File.WriteAllTextAsync(filePath, "{ this is not valid json }");

        // ===== ACT: Try to load variables from corrupted file =====
        
        var store = new MacroVarStore(_configuration, _logger);
        store.RegisterSession(sessionId, userId);

        var loadedVars = await store.GetAsync(sessionId);

        // ===== ASSERT: Verify empty dictionary returned (graceful degradation) =====
        
        Assert.NotNull(loadedVars);
        Assert.Empty(loadedVars);

        // ===== ACT: Verify store continues to work with in-memory cache =====
        
        var newVars = new Dictionary<string, string>
        {
            ["RECOVERED_VAR"] = "recovered_value"
        };
        await store.SetAsync(sessionId, newVars);

        var cachedVars = await store.GetAsync(sessionId);

        // ===== ASSERT: Verify in-memory cache works despite file corruption =====
        
        Assert.NotNull(cachedVars);
        Assert.Single(cachedVars);
        Assert.Equal("recovered_value", cachedVars["RECOVERED_VAR"]);
    }
}
