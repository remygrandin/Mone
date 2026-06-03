namespace Mone.Dashboard.Models;

public sealed record MetricSeriesPoint(DateTimeOffset Timestamp, double Value);

public sealed record MetricSeriesResponse(
    string MetricKey,
    int Count,
    double? Min,
    double? Max,
    double? Avg,
    double? Latest,
    DateTimeOffset? LatestTimestamp,
    MetricSeriesPoint[] Points);
