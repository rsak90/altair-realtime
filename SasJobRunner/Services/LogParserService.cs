using System.Text.RegularExpressions;
using SasJobRunner.Models;

namespace SasJobRunner.Services;

public sealed class LogParserService
{
    // Regex matching lines like:  MYVAR=hello world
    private static readonly Regex UserVarRegex =
        new(@"^([A-Z_][A-Z0-9_]*)=(.*)$", RegexOptions.Compiled | RegexOptions.Multiline);
    
    // Regex matching lines like:  GLOBAL MYVAR hello world  (for %put user; output)
    private static readonly Regex GlobalVarRegex =
        new(@"^GLOBAL\s+([A-Z_][A-Z0-9_]*)\s+(.*)$", RegexOptions.Compiled | RegexOptions.IgnoreCase);

    /// <summary>
    /// Scans log lines for the %put _user_; or %put user; output block.
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
            
            var trimmed = line.TrimStart();
            
            // Look for the start of the _user_ block (SESSIONID= line or GLOBAL SESSIONID)
            if (trimmed.StartsWith("SESSIONID=", StringComparison.OrdinalIgnoreCase) ||
                trimmed.StartsWith("GLOBAL SESSIONID", StringComparison.OrdinalIgnoreCase))
            {
                inBlock = true;
                Console.WriteLine($"[LogParser] Found SESSIONID line at line {lineCount}: {line}");
            }
            
            if (!inBlock) continue;
            
            // Try format 1: MYVAR=value (from %put _user_;)
            var m1 = UserVarRegex.Match(trimmed);
            if (m1.Success)
            {
                result[m1.Groups[1].Value] = m1.Groups[2].Value.TrimEnd();
                parsedLines++;
                Console.WriteLine($"[LogParser] Parsed variable (format 1): {m1.Groups[1].Value} = {m1.Groups[2].Value.TrimEnd()}");
                continue;
            }
            
            // Try format 2: GLOBAL MYVAR value (from %put user;)
            var m2 = GlobalVarRegex.Match(trimmed);
            if (m2.Success)
            {
                result[m2.Groups[1].Value] = m2.Groups[2].Value.TrimEnd();
                parsedLines++;
                Console.WriteLine($"[LogParser] Parsed variable (format 2): {m2.Groups[1].Value} = {m2.Groups[2].Value.TrimEnd()}");
                continue;
            }
            
            // If we're in block and the line doesn't match either format and is not whitespace, end the block
            if (inBlock && !string.IsNullOrWhiteSpace(trimmed))
            {
                // Check if it's a NOTE: line that would indicate end of the variable block
                if (trimmed.StartsWith("NOTE:", StringComparison.OrdinalIgnoreCase))
                {
                    Console.WriteLine($"[LogParser] End of _user_ block detected at line {lineCount}: {line}");
                    inBlock = false;
                }
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
