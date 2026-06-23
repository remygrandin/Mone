using Cronos;
using Mone.Infrastructure.Data.Entities;

namespace Mone.Infrastructure.Maintenance;

public static class MaintenanceWindowEvaluator
{
    public static bool IsInMaintenance(IEnumerable<MaintenanceWindowEntity> windows, DateTimeOffset nowUtc)
    {
        foreach (var window in windows)
        {
            if (!window.Enabled)
            {
                continue;
            }

            if (IsWindowActive(window, nowUtc))
            {
                return true;
            }
        }

        return false;
    }

    private static bool IsWindowActive(MaintenanceWindowEntity window, DateTimeOffset nowUtc)
    {
        return window.Kind switch
        {
            MaintenanceWindowKind.Manual => IsManualActive(window, nowUtc),
            MaintenanceWindowKind.RecurringCron => IsRecurringActive(window, nowUtc),
            _ => false,
        };
    }

    private static bool IsManualActive(MaintenanceWindowEntity window, DateTimeOffset nowUtc)
    {
        var started = window.StartsAt is null || nowUtc >= window.StartsAt.Value;
        var notExpired = window.ExpiresAt is null || nowUtc < window.ExpiresAt.Value;
        return started && notExpired;
    }

    private static bool IsRecurringActive(MaintenanceWindowEntity window, DateTimeOffset nowUtc)
    {
        if (string.IsNullOrWhiteSpace(window.Cron))
        {
            return false;
        }

        CronExpression expression;
        try
        {
            expression = CronExpression.Parse(window.Cron);
        }
        catch (CronFormatException)
        {
            return false;
        }

        var now = nowUtc.UtcDateTime;
        var duration = TimeSpan.FromMinutes(window.DurationMinutes);
        var occurrence = expression.GetNextOccurrence(now - duration, inclusive: true);

        return occurrence is { } fired
            && fired <= now
            && now < fired + duration;
    }
}
