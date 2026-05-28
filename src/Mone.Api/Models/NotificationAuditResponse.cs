using Mone.Contracts.Models;
using Mone.Infrastructure.Data.Entities;

namespace Mone.Api.Models;

public sealed record NotificationAuditResponse(
    Guid Id,
    DateTimeOffset Timestamp,
    Guid TargetId,
    string CheckerId,
    string NotificationPluginId,
    DeliveryStatus DeliveryStatus,
    string? ErrorMessage,
    int RetryCount,
    MonitoringStatus PreviousMonitoringStatus,
    MonitoringStatus CurrentMonitoringStatus);
