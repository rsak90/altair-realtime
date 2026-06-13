using SasJobRunner.Models;

namespace SasJobRunner.ViewModels;

public class SessionViewModel
{
    public string? ActiveSessionId { get; init; }
    public IReadOnlyList<SessionInfo> PastSessions { get; init; } = [];
}
