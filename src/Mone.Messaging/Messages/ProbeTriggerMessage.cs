namespace Mone.Messaging.Messages;

public sealed record ProbeTriggerMessage(
    Guid HostId,
    string ProbePluginId,
    Guid AssignmentId,
    string? TargetAddressOverride,
    string? HostAddress = null);
