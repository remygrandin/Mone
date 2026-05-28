using Mone.Dashboard.Models;
using MudBlazor;

namespace Mone.Dashboard.Helpers;

public static class StatusColorHelper
{
    public static Color ToMudColor(MonitoringStatus status) => status switch
    {
        MonitoringStatus.Healthy => Color.Success,
        MonitoringStatus.Degraded => Color.Warning,
        MonitoringStatus.Unhealthy => Color.Error,
        MonitoringStatus.Unreachable => Color.Dark,
        _ => Color.Default
    };

    public static string ToLabel(MonitoringStatus status) => status switch
    {
        MonitoringStatus.Healthy => "Healthy",
        MonitoringStatus.Degraded => "Degraded",
        MonitoringStatus.Unhealthy => "Unhealthy",
        MonitoringStatus.Unreachable => "Unreachable",
        _ => "Unknown"
    };
}
