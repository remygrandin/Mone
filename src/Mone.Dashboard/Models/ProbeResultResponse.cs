namespace Mone.Dashboard.Models;

public sealed record ProbeResultResponse(
    DateTimeOffset Timestamp,
    Guid TargetId,
    string ProbeId,
    MonitoringStatus Status,
    string Summary,
    double DurationMs,
    string? MetadataJson);
