namespace Mone.Dashboard.Models;

public sealed record ExecutorNodeResponse(
    Guid Id,
    string Name,
    string? Hostname,
    string? Address,
    string[] Roles,
    string? Version,
    DateTimeOffset? LastHeartbeatAt,
    DateTimeOffset RegisteredAt,
    string Health,
    int HeartbeatIntervalSeconds);

public sealed record RenameExecutorNodeRequest(string Name);
