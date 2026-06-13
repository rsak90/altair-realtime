using System.Collections.Concurrent;
using SasJobRunner.Models;

namespace SasJobRunner.Services;

public sealed class ProgramHistoryStore : IProgramHistoryStore
{
    private readonly ConcurrentDictionary<string, List<ProgramHistoryRecord>> _store = new();

    public Task AddAsync(ProgramHistoryRecord record)
    {
        _store.AddOrUpdate(record.UserId,
            _ => [record],
            (_, list) => { lock (list) { list.Add(record); } return list; });
        return Task.CompletedTask;
    }

    public Task<IReadOnlyList<ProgramHistoryRecord>> GetByUserAsync(string userId)
    {
        if (!_store.TryGetValue(userId, out var list))
            return Task.FromResult<IReadOnlyList<ProgramHistoryRecord>>([]);
        IReadOnlyList<ProgramHistoryRecord> sorted;
        lock (list)
            sorted = [.. list.OrderByDescending(r => r.SubmittedAt)];
        return Task.FromResult(sorted);
    }
}
