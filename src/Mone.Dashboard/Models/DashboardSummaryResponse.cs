namespace Mone.Dashboard.Models;

public sealed record DashboardSummaryResponse(
    int TotalHosts,
    int Healthy,
    int Degraded,
    int Unhealthy,
    int Unreachable,
    int Unknown);
