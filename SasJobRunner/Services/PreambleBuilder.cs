using System.Text;

namespace SasJobRunner.Services;

public sealed class PreambleBuilder
{
    private readonly string _studyFolder;

    public PreambleBuilder(IConfiguration configuration)
    {
        _studyFolder = configuration["SessionStorage:StudyFolder"] 
            ?? throw new InvalidOperationException("SessionStorage:StudyFolder configuration is required.");
    }

    /// <summary>
    /// Builds the Session_Preamble block. Pure function — no I/O.
    /// Creates path: {StudyFolder}/sessions/{userId}/{sessionId}/
    /// </summary>
    public string Build(
        string userId,
        string sessionId,
        IReadOnlyDictionary<string, string> macroVars)
    {
        var sb = new StringBuilder();
        // Ensure study folder doesn't end with slash, and construct full path
        var baseFolder = _studyFolder.TrimEnd('/');
        sb.AppendLine($"""LIBNAME SESSLIB "{baseFolder}/sessions/{userId}/{sessionId}/";""");
        sb.AppendLine(MacroLetBuilder.Build("SESSIONID", sessionId));
        foreach (var (name, value) in macroVars)
            sb.AppendLine(MacroLetBuilder.Build(name, value));
        return sb.ToString();
    }
}
