using Mone.Contracts.Models;

namespace Mone.Infrastructure.Data.Entities;

public enum DeliveryStatus
{
    Sent,
    Error
}

public sealed class NotificationAuditEntity
{
    public Guid Id { get; set; }
    public DateTimeOffset Timestamp { get; set; }
    public Guid TargetId { get; set; }
    public required string CheckerId { get; set; }
    public required string NotificationPluginId { get; set; }
    public DeliveryStatus DeliveryStatus { get; set; }
    public string? ErrorMessage { get; set; }
    public int RetryCount { get; set; }
    public MonitoringStatus PreviousMonitoringStatus { get; set; }
    public MonitoringStatus CurrentMonitoringStatus { get; set; }
}
