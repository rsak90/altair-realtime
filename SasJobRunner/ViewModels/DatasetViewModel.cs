using SasJobRunner.Models;

namespace SasJobRunner.ViewModels;

public class DatasetViewModel
{
    public string? SessionId { get; init; }
    public IReadOnlyList<string> AvailableDatasets { get; init; } = [];
    public string? SelectedDataset { get; init; }
    public PagedResult<DatasetRow>? PagedData { get; init; }
    public string? SortColumn { get; init; }
    public string SortDirection { get; init; } = "asc";
}
