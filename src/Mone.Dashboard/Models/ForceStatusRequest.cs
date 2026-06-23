namespace Mone.Dashboard.Models;

public sealed record ForceStatusRequest(string CheckerPluginId, MonitoringStatus Status);
