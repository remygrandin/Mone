using Mone.Infrastructure.Data.Entities;

namespace Mone.Api.Models;

public sealed record CreateMaintenanceWindowRequest(
    MaintenanceWindowKind Kind,
    DateTimeOffset? StartsAt,
    int DurationMinutes,
    string? Cron = null);
