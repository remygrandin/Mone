namespace Mone.Contracts.Models;

public sealed record StatusChange(
    string TargetId,
    MonitoringStatus PreviousStatus,
    MonitoringStatus CurrentStatus,
    ProbeResult LatestResult,
    DateTimeOffset ChangedAt);
