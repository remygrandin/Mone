namespace Mone.Dashboard.Models;

public sealed record CreateProbeAssignmentRequest(
    string ProbePluginId,
    string Name,
    string ScheduleCron,
    string? ConfigJson = null,
    string? TargetAddressOverride = null,
    bool Enabled = true,
    Guid? ExecutorNodeId = null);

public sealed record UpdateProbeAssignmentRequest(
    string ProbePluginId,
    string Name,
    string ScheduleCron,
    string? ConfigJson = null,
    string? TargetAddressOverride = null,
    bool Enabled = true,
    Guid? ExecutorNodeId = null);

public sealed record TriggerProbeRequest(string ProbePluginId);
