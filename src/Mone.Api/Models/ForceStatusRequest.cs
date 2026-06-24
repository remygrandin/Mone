namespace Mone.Api.Models;

using Mone.Contracts.Models;

public sealed record ForceStatusRequest(Guid AssignmentId, MonitoringStatus Status);
