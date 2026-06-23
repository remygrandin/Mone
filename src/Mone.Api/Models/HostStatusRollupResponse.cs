using Mone.Contracts.Models;

namespace Mone.Api.Models;

public sealed record HostStatusRollupResponse(
    Guid HostId,
    MonitoringStatus Status,
    int ErrorThreshold,
    int CheckerCount,
    int ErrorCount,
    int WarningCount);
