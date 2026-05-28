using Mone.Contracts.Models;

namespace Mone.Infrastructure.Data.Entities;

public sealed class ProbeResultEntity
{
    public DateTimeOffset Timestamp { get; set; }
    public Guid TargetId { get; set; }
    public required string ProbeId { get; set; }
    public MonitoringStatus Status { get; set; }
    public required string Summary { get; set; }
    public double DurationMs { get; set; }
    public string? MetadataJson { get; set; }
}
