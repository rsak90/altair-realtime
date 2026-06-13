namespace SasJobRunner.Models;

/// <summary>
/// Represents metadata for a dataset file in the session working directory.
/// Requirements: 8.1, 8.10
/// </summary>
public record DatasetFileInfo(
    string Name,
    long SizeBytes,
    DateTime LastModified
);
