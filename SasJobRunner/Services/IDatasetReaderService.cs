using SasJobRunner.Models;

namespace SasJobRunner.Services;

/// <summary>
/// Service for reading and querying SAS dataset files
/// </summary>
public interface IDatasetReaderService
{
    /// <summary>
    /// Gets metadata about a dataset without reading all rows
    /// </summary>
    Task<DatasetMetadata> GetMetadataAsync(
        string userId,
        string sessionId,
        string datasetName,
        CancellationToken ct = default);

    /// <summary>
    /// Reads dataset rows with optional filtering, sorting, and pagination
    /// </summary>
    Task<PagedResult<DatasetRow>> GetRowsAsync(
        string userId,
        string sessionId,
        string datasetName,
        DatasetFilterRequest request,
        CancellationToken ct = default);
}
