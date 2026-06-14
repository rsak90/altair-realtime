namespace SasJobRunner.Models;

/// <summary>
/// Request model for filtering and sorting datasets
/// </summary>
public record DatasetFilterRequest(
    int Page = 1,
    int PageSize = 100,
    string? SortColumn = null,
    bool SortAscending = true,
    IReadOnlyList<ColumnFilter>? Filters = null
);

/// <summary>
/// Filter criteria for a specific column
/// </summary>
public record ColumnFilter(
    string ColumnName,
    string Operator, // equals, contains, startsWith, endsWith, gt, lt, gte, lte
    string Value
);
