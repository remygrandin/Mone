using Mone.Contracts.Models;

namespace Mone.Infrastructure.Services;

/// <summary>
/// Server-authoritative N-of-M host status rollup (D063). Pure and deterministic:
/// a verdict is fully reproducible from the per-checker status list plus the error threshold.
/// </summary>
public static class HostStatusRollup
{
    /// <summary>
    /// Rolls up a host's latest per-checker statuses into a single host status using an
    /// N-of-M error policy. <paramref name="errorThreshold"/> below 1 is clamped to 1.
    /// </summary>
    public static MonitoringStatus Compute(
        IReadOnlyCollection<MonitoringStatus> latestStatuses,
        int errorThreshold) => ComputeResult(latestStatuses, errorThreshold).Status;

    /// <summary>
    /// Rolls up per-checker statuses and returns the diagnostic counts that explain the verdict
    /// (checker/error/warning counts) so callers can surface WHY a host rolled up to a status.
    /// </summary>
    public static HostRollupResult ComputeResult(
        IReadOnlyCollection<MonitoringStatus> latestStatuses,
        int errorThreshold)
    {
        var threshold = Math.Max(errorThreshold, 1);
        var checkerCount = latestStatuses.Count;

        if (checkerCount == 0)
        {
            return new HostRollupResult(MonitoringStatus.Unknown, 0, 0, 0);
        }

        var errorCount = 0;
        var warningCount = 0;
        var anyHealthy = false;

        foreach (var status in latestStatuses)
        {
            switch (status)
            {
                case MonitoringStatus.Unhealthy:
                case MonitoringStatus.Unreachable:
                    errorCount++;
                    break;
                case MonitoringStatus.Degraded:
                    warningCount++;
                    break;
                case MonitoringStatus.Healthy:
                    anyHealthy = true;
                    break;
            }
        }

        MonitoringStatus rolled;
        if (errorCount >= threshold)
        {
            rolled = MonitoringStatus.Unhealthy;
        }
        else if (errorCount >= 1)
        {
            rolled = MonitoringStatus.Degraded;
        }
        else if (warningCount >= 1)
        {
            rolled = MonitoringStatus.Degraded;
        }
        else if (anyHealthy)
        {
            rolled = MonitoringStatus.Healthy;
        }
        else
        {
            rolled = MonitoringStatus.Unknown;
        }

        return new HostRollupResult(rolled, checkerCount, errorCount, warningCount);
    }
}

/// <summary>
/// Diagnostic outcome of a host status rollup: the final status plus the counts that explain it.
/// </summary>
public readonly record struct HostRollupResult(
    MonitoringStatus Status,
    int CheckerCount,
    int ErrorCount,
    int WarningCount);
