namespace Mone.Dashboard.Models;

public sealed record StatusResponse(
    DateTimeOffset Timestamp,
    Guid TargetId,
    string CheckerId,
    MonitoringStatus PreviousStatus,
    MonitoringStatus CurrentStatus);
