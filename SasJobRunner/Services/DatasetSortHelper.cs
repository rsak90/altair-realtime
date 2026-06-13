using SasJobRunner.Models;

namespace SasJobRunner.Services;

public static class DatasetSortHelper
{
    /// <summary>
    /// Sorts dataset rows by the specified column key.
    /// Rows missing the column key are treated as empty string.
    /// </summary>
    public static IReadOnlyList<DatasetRow> Sort(
        IEnumerable<DatasetRow> rows,
        string columnKey,
        bool descending = false)
    {
        var sorted = rows.OrderBy(
            r => r.Columns.TryGetValue(columnKey, out var v) ? v : string.Empty,
            StringComparer.OrdinalIgnoreCase);
        return descending
            ? sorted.Reverse().ToList()
            : sorted.ToList();
    }

    /// <summary>
    /// Paginates a list at the given page size (default 100 rows/page).
    /// Page is 1-based.
    /// </summary>
    public static PagedResult<T> Paginate<T>(
        IReadOnlyList<T> items,
        int page,
        int pageSize = 100)
    {
        var totalCount = items.Count;
        var skip = (page - 1) * pageSize;
        var pageItems = items.Skip(skip).Take(pageSize).ToList();
        return new PagedResult<T>(pageItems, totalCount, page, pageSize);
    }
}
