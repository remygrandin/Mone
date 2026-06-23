using Mone.Infrastructure.Data.Entities;
using Mone.Infrastructure.Maintenance;
using Xunit;

namespace Mone.Infrastructure.Tests.Maintenance;

public sealed class MaintenanceWindowEvaluatorTests
{
    private static readonly DateTimeOffset Now = new(2026, 6, 18, 12, 15, 0, TimeSpan.Zero);

    private static MaintenanceWindowEntity Manual(
        DateTimeOffset? startsAt = null,
        DateTimeOffset? expiresAt = null,
        bool enabled = true) => new()
        {
            Id = Guid.NewGuid(),
            HostId = Guid.NewGuid(),
            Kind = MaintenanceWindowKind.Manual,
            StartsAt = startsAt,
            ExpiresAt = expiresAt,
            Enabled = enabled,
        };

    private static MaintenanceWindowEntity Recurring(
        string? cron,
        int durationMinutes,
        bool enabled = true) => new()
        {
            Id = Guid.NewGuid(),
            HostId = Guid.NewGuid(),
            Kind = MaintenanceWindowKind.RecurringCron,
            Cron = cron,
            DurationMinutes = durationMinutes,
            Enabled = enabled,
        };

    [Fact]
    public void Manual_NoBounds_IsActive()
    {
        Assert.True(MaintenanceWindowEvaluator.IsInMaintenance(new[] { Manual() }, Now));
    }

    [Fact]
    public void Manual_NotYetStarted_IsNotActive()
    {
        var window = Manual(startsAt: Now.AddMinutes(5));
        Assert.False(MaintenanceWindowEvaluator.IsInMaintenance(new[] { window }, Now));
    }

    [Fact]
    public void Manual_AtStart_IsActive()
    {
        var window = Manual(startsAt: Now);
        Assert.True(MaintenanceWindowEvaluator.IsInMaintenance(new[] { window }, Now));
    }

    [Fact]
    public void Manual_Expired_IsNotActive_AutoExitResume()
    {
        var window = Manual(startsAt: Now.AddMinutes(-30), expiresAt: Now);
        Assert.False(MaintenanceWindowEvaluator.IsInMaintenance(new[] { window }, Now));
    }

    [Fact]
    public void Manual_BeforeExpiry_IsActive()
    {
        var window = Manual(startsAt: Now.AddMinutes(-30), expiresAt: Now.AddMinutes(5));
        Assert.True(MaintenanceWindowEvaluator.IsInMaintenance(new[] { window }, Now));
    }

    [Fact]
    public void Manual_Disabled_IsNotActive()
    {
        var window = Manual(enabled: false);
        Assert.False(MaintenanceWindowEvaluator.IsInMaintenance(new[] { window }, Now));
    }

    [Fact]
    public void Recurring_InsideWindow_IsActive()
    {
        // Fires daily at 12:00; 30-minute window; now is 12:15.
        var window = Recurring("0 12 * * *", durationMinutes: 30);
        Assert.True(MaintenanceWindowEvaluator.IsInMaintenance(new[] { window }, Now));
    }

    [Fact]
    public void Recurring_OutsideWindow_IsNotActive()
    {
        // Fires daily at 12:00; 5-minute window; now is 12:15 (beyond fire+duration).
        var window = Recurring("0 12 * * *", durationMinutes: 5);
        Assert.False(MaintenanceWindowEvaluator.IsInMaintenance(new[] { window }, Now));
    }

    [Fact]
    public void Recurring_Disabled_IsNotActive()
    {
        var window = Recurring("0 12 * * *", durationMinutes: 30, enabled: false);
        Assert.False(MaintenanceWindowEvaluator.IsInMaintenance(new[] { window }, Now));
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData("not a cron")]
    [InlineData("0 12 * *")]
    public void Recurring_InvalidOrEmptyCron_IsNotActive_NoThrow(string? cron)
    {
        var window = Recurring(cron, durationMinutes: 30);
        Assert.False(MaintenanceWindowEvaluator.IsInMaintenance(new[] { window }, Now));
    }

    [Fact]
    public void MultipleWindows_OneActive_IsActive()
    {
        var windows = new[]
        {
            Manual(startsAt: Now.AddMinutes(5)),  // not yet started
            Recurring("0 12 * * *", durationMinutes: 5), // outside window
            Manual(),                              // active
        };
        Assert.True(MaintenanceWindowEvaluator.IsInMaintenance(windows, Now));
    }

    [Fact]
    public void NoWindows_IsNotActive()
    {
        Assert.False(MaintenanceWindowEvaluator.IsInMaintenance(Array.Empty<MaintenanceWindowEntity>(), Now));
    }
}
