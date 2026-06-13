namespace SasJobRunner.Models;

/// <summary>
/// Represents a result file returned by the Altair SLC Hub after a job completes.
/// </summary>
public record JobResultFile(string Name, string Url);
