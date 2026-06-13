namespace SasJobRunner.Models;

public record ProgramHistoryRecord(
    string RecordId,                         // UUID
    string UserId,
    string SessionId,
    DateTime SubmittedAt,
    string SourceCode,                       // user-supplied portion only
    string LogSummary,                       // first ERROR/WARNING, or "Completed"
    IReadOnlyList<string> DatasetsProduced
);
