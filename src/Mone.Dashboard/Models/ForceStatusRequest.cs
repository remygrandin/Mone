namespace Mone.Dashboard.Models;

public sealed record ForceStatusRequest(Guid AssignmentId, MonitoringStatus Status);
