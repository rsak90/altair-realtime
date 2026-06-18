using System.Text.RegularExpressions;
using Microsoft.Extensions.Logging;
using SasJobRunner.Models;

namespace SasJobRunner.Services;

public sealed class LogParserService(ILogger<LogParserService> logger)
{
    private static readonly Regex UserVarRegex =
        new(@"^([A-Z_][A-Z0-9_]*)=(.*)$", RegexOptions.Compiled | RegexOptions.Multiline);

    private static readonly Regex GlobalVarRegex =
        new(@"^GLOBAL\s+([A-Z_][A-Z0-9_]*)\s+(.*)$", RegexOptions.Compiled | RegexOptions.IgnoreCase);

    private static readonly Regex MacroStartRegex =
        new(@"=== MACRO_SOURCE_START:\s*([A-Z_][A-Z0-9_]*)\s*===", RegexOptions.Compiled | RegexOptions.IgnoreCase);

    private static readonly Regex MacroEndRegex =
        new(@"=== MACRO_SOURCE_END:\s*([A-Z_][A-Z0-9_]*)\s*===", RegexOptions.Compiled | RegexOptions.IgnoreCase);

    /// <summary>
    /// Generates SAS postamble code that enumerates WORK.SASMACR macros and writes their source to the log.
    /// </summary>
    public static string GenerateMacroCatalogExtractionCode() =>
        """
        /* === MACRO CATALOG EXTRACTION START === */
        proc catalog catalog=work.sasmacr;
           contents;
        quit;

        %macro _extract_macros;
           %local _i _name;
           proc sql noprint;
              select objname into :_name separated by '|'
              from dictionary.catalogs
              where libname='WORK' and memname='SASMACR' and objtype='MACRO';
           quit;

           %let _i = 1;
           %let _name = %scan(&_name, &_i, '|');
           %do %while("&_name" ne "");
              %if %substr(&_name, 1, 4) ne SYS_ %then %do;
                 %put === MACRO_SOURCE_START: &_name ===;
                 %copy &_name / source;
                 %put === MACRO_SOURCE_END: &_name ===;
              %end;
              %let _i = %eval(&_i + 1);
              %let _name = %scan(&_name, &_i, '|');
           %end;
        %mend _extract_macros;

        %_extract_macros;
        /* === MACRO CATALOG EXTRACTION END === */
        """;

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

            if (line.Contains("MPRINT") || line.Contains("MLOGIC"))
            {
                skippedLines++;
                continue;
            }

            var trimmed = line.TrimStart();

            if (!inBlock)
            {
                if (trimmed.StartsWith("SESSIONID=", StringComparison.OrdinalIgnoreCase) ||
                    trimmed.StartsWith("GLOBAL SESSIONID", StringComparison.OrdinalIgnoreCase) ||
                    trimmed.StartsWith("GLOBAL ", StringComparison.OrdinalIgnoreCase))
                {
                    inBlock = true;
                    Console.WriteLine($"[LogParser] Found start of user variables block at line {lineCount}: {line}");
                }
            }

            if (!inBlock) continue;

            var m1 = UserVarRegex.Match(trimmed);
            if (m1.Success)
            {
                result[m1.Groups[1].Value] = m1.Groups[2].Value.TrimEnd();
                parsedLines++;
                Console.WriteLine($"[LogParser] Parsed variable (format 1): {m1.Groups[1].Value} = {m1.Groups[2].Value.TrimEnd()}");
                continue;
            }

            var m2 = GlobalVarRegex.Match(trimmed);
            if (m2.Success)
            {
                result[m2.Groups[1].Value] = m2.Groups[2].Value.TrimEnd();
                parsedLines++;
                Console.WriteLine($"[LogParser] Parsed variable (format 2): {m2.Groups[1].Value} = {m2.Groups[2].Value.TrimEnd()}");
                continue;
            }

            if (inBlock && !string.IsNullOrWhiteSpace(trimmed))
            {
                Console.WriteLine($"[LogParser] In block but line doesn't match pattern at line {lineCount}: {line}");

                if (trimmed.StartsWith("NOTE:", StringComparison.OrdinalIgnoreCase))
                {
                    Console.WriteLine($"[LogParser] End of _user_ block detected at line {lineCount}: {line}");
                    inBlock = false;
                }
            }
        }

        Console.WriteLine($"[LogParser] Summary: Total lines={lineCount}, Skipped={skippedLines}, Parsed={parsedLines}, Result count={result.Count}");

        Console.WriteLine("[LogParser] All parsed variables:");
        foreach (var kvp in result)
            Console.WriteLine($"[LogParser]   {kvp.Key} = {kvp.Value}");

        return result;
    }

    /// <summary>
    /// Parses macro catalog extraction output from job log.
    /// Looks for macro extraction markers and reconstructs macro source code.
    /// </summary>
    public Dictionary<string, string> ParseMacroCatalog(IEnumerable<string> logLines)
    {
        var result = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        string? currentMacroName = null;
        var currentLines = new List<string>();

        foreach (var line in logLines)
        {
            var startMatch = MacroStartRegex.Match(line);
            if (startMatch.Success)
            {
                var macroName = startMatch.Groups[1].Value;

                if (macroName.StartsWith("SYS_", StringComparison.OrdinalIgnoreCase))
                    continue;

                if (currentMacroName != null)
                {
                    logger.LogWarning(
                        "Macro catalog parsing encountered a new start marker for '{NewMacroName}' before end marker for '{PreviousMacroName}'. Skipping incomplete macro.",
                        macroName, currentMacroName);
                }

                currentMacroName = macroName;
                currentLines.Clear();
                continue;
            }

            if (currentMacroName != null)
            {
                var endMatch = MacroEndRegex.Match(line);
                if (endMatch.Success)
                {
                    var endName = endMatch.Groups[1].Value;
                    if (!endName.Equals(currentMacroName, StringComparison.OrdinalIgnoreCase))
                    {
                        logger.LogWarning(
                            "Macro catalog parsing found mismatched end marker '{EndName}' for macro '{StartName}'. Skipping macro.",
                            endName, currentMacroName);
                        currentMacroName = null;
                        currentLines.Clear();
                        continue;
                    }

                    var source = string.Join(Environment.NewLine, currentLines).Trim();
                    if (!string.IsNullOrWhiteSpace(source))
                        result[currentMacroName] = source;

                    currentMacroName = null;
                    currentLines.Clear();
                    continue;
                }

                currentLines.Add(ExtractLogMessageContent(line));
            }
        }

        if (currentMacroName != null)
        {
            logger.LogWarning(
                "Macro catalog parsing found incomplete macro '{MacroName}' (missing end marker). Skipping macro.",
                currentMacroName);
        }

        logger.LogDebug("Parsed {MacroCount} macro programs from job log", result.Count);
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
            if (t.StartsWith("ERROR", StringComparison.OrdinalIgnoreCase)) return line;
            if (t.StartsWith("WARNING", StringComparison.OrdinalIgnoreCase)) return line;
        }
        return "Completed";
    }

    private static string ExtractLogMessageContent(string line)
    {
        var trimmed = line.TrimStart();

        if (trimmed.StartsWith("NOTE:", StringComparison.OrdinalIgnoreCase))
            return trimmed["NOTE:".Length..].TrimStart();

        if (trimmed.StartsWith("MLOGIC(", StringComparison.OrdinalIgnoreCase))
            return trimmed;

        return line;
    }
}
