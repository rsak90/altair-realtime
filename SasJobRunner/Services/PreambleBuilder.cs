using System.Text;

namespace SasJobRunner.Services;

public sealed class PreambleBuilder
{
    /// <summary>
    /// Builds the Session_Preamble block. Pure function — no I/O.
    /// </summary>
    public string Build(
        string userId,
        string sessionId,
        IReadOnlyDictionary<string, string> macroVars)
    {
        var sb = new StringBuilder();
        sb.AppendLine($"""LIBNAME SESSLIB "/sas/sessions/{userId}/{sessionId}/";""");
        sb.AppendLine(MacroLetBuilder.Build("SESSIONID", sessionId));
        foreach (var (name, value) in macroVars)
            sb.AppendLine(MacroLetBuilder.Build(name, value));
        return sb.ToString();
    }
}
