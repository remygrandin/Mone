namespace Mone.Contracts.Models;

public sealed record CheckerEvaluationContext(
    string TargetId,
    string? TriggeringProbeId,
    ProbeResult? TriggeringResult,
    IMetricHistoryAccessor History,
    CancellationToken CancellationToken);
