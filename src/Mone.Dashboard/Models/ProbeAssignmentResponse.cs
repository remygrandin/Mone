namespace Mone.Dashboard.Models;

public sealed record ProbeAssignmentResponse(
    Guid Id,
    Guid HostId,
    string ProbePluginId,
    string ScheduleCron,
    string? ConfigJson,
    bool Enabled);
