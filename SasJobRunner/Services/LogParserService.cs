using System.Text.RegularExpressions;
using SasJobRunner.Models;

namespace SasJobRunner.Services;

public sealed class LogParserService
{
    // Regex matching lines like:  MYVAR=hello world
    private static readonly Regex UserVarRegex =
        new(@"^([A-Z_][A-Z0-9_]*)=(.*)$", RegexOptions.Compiled | RegexOptions.Multiline);

    /// <summary>
    /// Scans log lines for the %put _user_; output block.
    /// Returns all parsed name-value pairs (may be empty).
    /// </summary>
    public Dictionary<string, string> ParseUserMacroVars(IEnumerable<string> logLines)
    {
        var result = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        var inBlock = false;
        var lineCount = 0;
        var skippedLines = 0;
        var parsedLines = 0;
        
        foreach (var line in logLines)
        {
            lineCount++;
            
            // Skip MPRINT and MLOGIC lines
            if (line.Contains("MPRINT") || line.Contains("MLOGIC"))
            {
                skippedLines++;
                continue;
            }
            
            // Look for the start of the _user_ block (SESSIONID= line)
            if (line.TrimStart().StartsWith("SESSIONID=", StringComparison.OrdinalIgnoreCase))
            {
                inBlock = true;
                Console.WriteLine($"[LogParser] Found SESSIONID= line at line {lineCount}: {line}");
            }
            
            if (!inBlock) continue;
            
            var m = UserVarRegex.Match(line.TrimStart());
            if (m.Success)
            {
                result[m.Groups[1].Value] = m.Groups[2].Value.TrimEnd();
                parsedLines++;
                Console.WriteLine($"[LogParser] Parsed variable: {m.Groups[1].Value} = {m.Groups[2].Value.TrimEnd()}");
            }
            else if (inBlock && !string.IsNullOrWhiteSpace(line))
            {
                Console.WriteLine($"[LogParser] End of _user_ block detected at line {lineCount}: {line}");
                inBlock = false; // end of _user_ block
            }
        }
        
        Console.WriteLine($"[LogParser] Summary: Total lines={lineCount}, Skipped={skippedLines}, Parsed={parsedLines}, Result count={result.Count}");
        return result;
    }

    /// <summary>Classifies a raw log line by its severity prefix.</summary>
    public static LogLine Classify(string raw) => LogLine.Parse(raw);

    /// <summary>
    /// Returns the first ERROR or WARNING line text, or "Completed" if none.
    /// </summary>
    public static string Summarize(IEnumerable<string> logLines)
    {
        foreach (var line in logLines)
        {
            var t = line.TrimStart();
            if (t.StartsWith("ERROR",   StringComparison.OrdinalIgnoreCase)) return line;
            if (t.StartsWith("WARNING", StringComparison.OrdinalIgnoreCase)) return line;
        }
        return "Completed";
    }
}
