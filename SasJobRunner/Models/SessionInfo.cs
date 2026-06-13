namespace SasJobRunner.Models;

public record SessionInfo(
    string UserId,
    string SessionId,        // UUID string
    DateTime CreatedAt,
    DateTime LastAccessedAt
);
