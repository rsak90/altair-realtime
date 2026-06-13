using SasJobRunner.Models;

namespace SasJobRunner.ViewModels;

public class HistoryViewModel
{
    public IReadOnlyList<HistoryItemViewModel> Items { get; init; } = [];
}

public class HistoryItemViewModel
{
    public required string RecordId { get; init; }
    public required string UserId { get; init; }
    public required string SessionId { get; init; }
    public required DateTime SubmittedAt { get; init; }
    // Truncated to 120 characters (no ellipsis in this field — the view can append "...")
    public required string SourceCodePreview { get; init; }
    public required string LogSummary { get; init; }
    public required IReadOnlyList<string> DatasetsProduced { get; init; }

    public static HistoryItemViewModel FromRecord(ProgramHistoryRecord record) => new()
    {
        RecordId = record.RecordId,
        UserId = record.UserId,
        SessionId = record.SessionId,
        SubmittedAt = record.SubmittedAt,
        SourceCodePreview = record.SourceCode.Length > 120
            ? record.SourceCode[..120]
            : record.SourceCode,
        LogSummary = record.LogSummary,
        DatasetsProduced = record.DatasetsProduced
    };
}
