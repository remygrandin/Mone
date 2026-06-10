namespace Mone.Dashboard.Models;

public sealed record CheckerAssignmentResponse(
    Guid Id,
    Guid HostId,
    string CheckerPluginId,
    string Name,
    string NameSnakeCase,
    string? ConfigJson,
    bool Enabled,
    Guid? ExecutorNodeId = null);
