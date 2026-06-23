using System.ComponentModel.DataAnnotations;

namespace Mone.Infrastructure.Data.Entities;

public enum MaintenanceWindowKind
{
    Manual,
    RecurringCron
}

public sealed class MaintenanceWindowEntity
{
    public Guid Id { get; set; }
    public Guid HostId { get; set; }
    public HostEntity? Host { get; set; }
    public MaintenanceWindowKind Kind { get; set; }
    public DateTimeOffset? StartsAt { get; set; }
    public DateTimeOffset? ExpiresAt { get; set; }
    [MaxLength(64)]
    public string? Cron { get; set; }
    public int DurationMinutes { get; set; }
    public bool Enabled { get; set; } = true;
    public DateTimeOffset CreatedAt { get; set; }
    public DateTimeOffset UpdatedAt { get; set; }
}
