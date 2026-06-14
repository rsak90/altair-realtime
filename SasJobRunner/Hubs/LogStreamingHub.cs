using Microsoft.AspNetCore.SignalR;

namespace SasJobRunner.Hubs;

public sealed class LogStreamingHub : Hub
{
    /// <summary>Subscribe to log lines for a specific job.</summary>
    public async Task JoinJob(string jobId) =>
        await Groups.AddToGroupAsync(Context.ConnectionId, jobId);

    /// <summary>Unsubscribe from a job's log stream.</summary>
    public async Task LeaveJob(string jobId) =>
        await Groups.RemoveFromGroupAsync(Context.ConnectionId, jobId);

    /// <summary>Subscribe to file change notifications for a session.</summary>
    public async Task JoinSession(string sessionId) =>
        await Groups.AddToGroupAsync(Context.ConnectionId, $"session_{sessionId}");

    /// <summary>Unsubscribe from session notifications.</summary>
    public async Task LeaveSession(string sessionId) =>
        await Groups.RemoveFromGroupAsync(Context.ConnectionId, $"session_{sessionId}");
}
