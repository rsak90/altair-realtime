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
        IReadOnlyDictionary<string, string> macroVars,
        string macroPrograms = "")
    {
        var sb = new StringBuilder();
        var baseFolder = _studyFolder.TrimEnd('/');

        sb.AppendLine("/* === SESSION LIBRARY DEFINITION === */");
        sb.AppendLine($"""LIBNAME SESSLIB "{baseFolder}/sessions/{userId}/{sessionId}/";""");

        if (!string.IsNullOrWhiteSpace(macroPrograms))
        {
            sb.AppendLine("/* === MACRO PROGRAM RESTORATION === */");
            sb.AppendLine(macroPrograms.TrimEnd());
            sb.AppendLine();
        }

        sb.AppendLine("/* === MACRO VARIABLE RESTORATION === */");
        sb.AppendLine(MacroLetBuilder.Build("SESSIONID", sessionId));
        foreach (var (name, value) in macroVars)
            sb.AppendLine(MacroLetBuilder.Build(name, value));

        return sb.ToString();
    }
}
