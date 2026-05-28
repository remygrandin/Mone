namespace Mone.Api.Models;

public sealed record UpdateProbeAssignmentRequest(
    string ProbePluginId,
    string ScheduleCron,
    string? ConfigJson = null,
    string? TargetAddressOverride = null,
    bool Enabled = true);
