namespace Mone.Api.Models;

public sealed record CheckerAssignmentResponse(
    Guid Id,
    Guid? HostId,
    Guid? GroupId,
    string CheckerPluginId,
    string? ConfigJson,
    bool Enabled);
