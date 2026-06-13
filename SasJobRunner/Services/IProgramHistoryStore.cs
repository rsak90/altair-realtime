using SasJobRunner.Models;

namespace SasJobRunner.Services;

public interface IProgramHistoryStore
{
    Task AddAsync(ProgramHistoryRecord record);

    /// <summary>Returns records for the given user, sorted by SubmittedAt descending.</summary>
    Task<IReadOnlyList<ProgramHistoryRecord>> GetByUserAsync(string userId);
}
