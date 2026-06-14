namespace SasJobRunner.Models;

/// <summary>
/// Metadata about a SAS dataset (.sas7bdat file)
/// </summary>
public record DatasetMetadata(
    string Name,
    int RowCount,
    int ColumnCount,
    IReadOnlyList<ColumnInfo> Columns,
    long FileSizeBytes,
    DateTime LastModified
);

/// <summary>
/// Information about a dataset column
/// </summary>
public record ColumnInfo(
    string Name,
    string Type,
    int Length,
    string? Format,
    string? Label
);
