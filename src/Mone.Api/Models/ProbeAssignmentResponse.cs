namespace Mone.Api.Models;

public sealed record ProbeAssignmentResponse(
    Guid Id,
    Guid? HostId,
    Guid? GroupId,
    string ProbePluginId,
    string Name,
    string NameSnakeCase,
    string ScheduleCron,
    string? ConfigJson,
    string? TargetAddressOverride,
    bool Enabled);
