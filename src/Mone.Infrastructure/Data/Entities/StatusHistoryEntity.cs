using Mone.Contracts.Models;

namespace Mone.Infrastructure.Data.Entities;

public sealed class StatusHistoryEntity
{
    public DateTimeOffset Timestamp { get; set; }
    public Guid TargetId { get; set; }
    public MonitoringStatus PreviousStatus { get; set; }
    public MonitoringStatus CurrentStatus { get; set; }
    public required string CheckerId { get; set; }
}
