using Mone.Infrastructure.Data.Entities;

namespace Mone.Api.Models;

public sealed record MaintenanceWindowResponse(
    Guid Id,
    Guid HostId,
    MaintenanceWindowKind Kind,
    DateTimeOffset? StartsAt,
    DateTimeOffset? ExpiresAt,
    string? Cron,
    int DurationMinutes,
    bool Enabled);
