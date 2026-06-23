namespace Mone.Api.Models;

using Mone.Contracts.Models;

public sealed record ForceStatusRequest(string CheckerPluginId, MonitoringStatus Status);
