namespace Mone.Dashboard.Models;

public sealed record CheckerAssignmentResponse(
    Guid Id,
    Guid HostId,
    string CheckerPluginId,
    string? ConfigJson,
    bool Enabled);
