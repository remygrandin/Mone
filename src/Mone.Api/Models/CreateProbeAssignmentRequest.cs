namespace Mone.Api.Models;

public sealed record CreateProbeAssignmentRequest(
    string ProbePluginId,
    string Name,
    string ScheduleCron,
    string? ConfigJson = null,
    string? TargetAddressOverride = null,
    bool Enabled = true,
    Guid? ExecutorNodeId = null);
