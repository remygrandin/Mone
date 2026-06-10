namespace Mone.Api.Models;

public sealed record CheckerAssignmentResponse(
    Guid Id,
    Guid? HostId,
    Guid? GroupId,
    string CheckerPluginId,
    string Name,
    string NameSnakeCase,
    string? ConfigJson,
    bool Enabled,
    Guid? ExecutorNodeId = null);
