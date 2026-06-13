namespace SasJobRunner.Models;

public enum LogSeverity { Plain, Note, Warning, Error }

public record LogLine(string Text, LogSeverity Severity)
{
    public static LogLine Parse(string raw) => raw.TrimStart() switch
    {
        var s when s.StartsWith("ERROR",   StringComparison.OrdinalIgnoreCase) => new(raw, LogSeverity.Error),
        var s when s.StartsWith("WARNING", StringComparison.OrdinalIgnoreCase) => new(raw, LogSeverity.Warning),
        var s when s.StartsWith("NOTE",    StringComparison.OrdinalIgnoreCase) => new(raw, LogSeverity.Note),
        _ => new(raw, LogSeverity.Plain)
    };
}
