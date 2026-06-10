namespace Mone.Api.Models;

public sealed record RegisterExecutorNodeRequest(
    Guid Id,
    string Name,
    string? Hostname,
    string? Address,
    int Role,
    string? Version);

public sealed record ExecutorNodeHeartbeatRequest(string? Version);

public sealed record RenameExecutorNodeRequest(string Name);

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
