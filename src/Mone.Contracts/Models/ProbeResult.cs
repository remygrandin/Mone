namespace Mone.Contracts.Models;

public sealed record ProbeResult(
    MonitoringStatus Status,
    string Summary,
    DateTimeOffset Timestamp,
    TimeSpan Duration,
    IReadOnlyDictionary<string, object>? Metadata = null);
