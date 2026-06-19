using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.SignalR;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;
using Moq;
using SasJobRunner.Hubs;
using SasJobRunner.Services;

namespace SasJobRunner.Tests;

public sealed class MacroProgramPersistenceTests : IDisposable
{
    private readonly string _tempStudyFolder;
    private readonly IConfiguration _configuration;
    private readonly ILoggerFactory _loggerFactory;

    public MacroProgramPersistenceTests()
    {
        _tempStudyFolder = Path.Combine(Path.GetTempPath(), "MacroProgramPersistenceTests_" + Guid.NewGuid());
        Directory.CreateDirectory(_tempStudyFolder);

        _configuration = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["SessionStorage:StudyFolder"] = _tempStudyFolder,
                ["SessionStorage:EnableMacroCatalogExtraction"] = "false"
            })
            .Build();

        _loggerFactory = LoggerFactory.Create(builder => builder.AddConsole().SetMinimumLevel(LogLevel.Debug));
    }

    public void Dispose()
    {
        _loggerFactory.Dispose();

        if (Directory.Exists(_tempStudyFolder))
            Directory.Delete(_tempStudyFolder, recursive: true);
    }

    [Fact]
    public void ParseMacroDefinitionsFromSource_CapturesSubmittedMacroBlocks()
    {
        var parser = new LogParserService(_loggerFactory.CreateLogger<LogParserService>());
        var source = """
            %macro greet(name);
               %put Hello, &name.;
            %mend greet;

            data work.example;
            run;

            %macro calc(a, b);
               %let result = %eval(&a + &b);
               %put &=result;
            %mend;
            """;

        var macros = parser.ParseMacroDefinitionsFromSource(source);

        Assert.Equal(2, macros.Count);
        Assert.Contains("%macro greet(name);", macros["greet"]);
        Assert.Contains("%mend greet;", macros["greet"]);
        Assert.Contains("%macro calc(a, b);", macros["calc"]);
        Assert.Contains("%mend;", macros["calc"]);
    }

    [Fact]
    public async Task MergeAsync_PreservesExistingMacrosAndReplacesMatchingNames()
    {
        var userId = "macro-user";
        var sessionId = "macro-session";
        var store = new MacroProgramStore(_configuration, _loggerFactory.CreateLogger<MacroProgramStore>());
        store.RegisterSession(sessionId, userId);

        await store.MergeAsync(sessionId, new Dictionary<string, string>
        {
            ["greet"] = """
                %macro greet(name);
                   %put Hello, &name.;
                %mend greet;
                """,
            ["oldmacro"] = """
                %macro oldmacro();
                   %put still here;
                %mend;
                """
        });

        await store.MergeAsync(sessionId, new Dictionary<string, string>
        {
            ["greet"] = """
                %macro greet(name);
                   %put Updated, &name.;
                %mend greet;
                """
        });

        var source = await store.GetAsync(sessionId);

        Assert.Contains("%put Updated, &name.;", source);
        Assert.DoesNotContain("%put Hello, &name.;", source);
        Assert.Contains("%macro oldmacro();", source);
        Assert.Contains("%mend;", source);
    }

    [Fact]
    public async Task SubmitAsync_DoesNotInjectCatalogExtractionByDefault()
    {
        var userId = "submit-user";
        var sessionId = "submit-session";
        string? submittedCode = null;

        var hubClient = new Mock<ISlcHubClient>();
        hubClient.Setup(x => x.CreateJobAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .Callback<string, CancellationToken>((code, _) => submittedCode = code)
            .ReturnsAsync("job-123");
        hubClient.Setup(x => x.CommitJobAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .Returns(Task.CompletedTask);

        var orchestrator = new SessionJobOrchestrator(
            hubClient.Object,
            new PreambleBuilder(_configuration),
            new MacroVarStore(_configuration, _loggerFactory.CreateLogger<MacroVarStore>()),
            new MacroProgramStore(_configuration, _loggerFactory.CreateLogger<MacroProgramStore>()),
            Mock.Of<IProgramHistoryStore>(),
            new LogParserService(_loggerFactory.CreateLogger<LogParserService>()),
            Mock.Of<IHubContext<LogStreamingHub>>(),
            Mock.Of<IHttpContextAccessor>(),
            _configuration,
            _loggerFactory.CreateLogger<SessionJobOrchestrator>());

        var jobId = await orchestrator.SubmitAsync(userId, sessionId, "%put hello;");

        Assert.Equal("job-123", jobId);
        Assert.NotNull(submittedCode);
        Assert.DoesNotContain("proc catalog catalog=work.sasmacr", submittedCode, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("%copy &_name / source", submittedCode, StringComparison.OrdinalIgnoreCase);
    }
}
