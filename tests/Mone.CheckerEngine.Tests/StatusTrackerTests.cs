using Mone.CheckerEngine.Services;
using Mone.Contracts.Models;
using Xunit;

namespace Mone.CheckerEngine.Tests;

public sealed class StatusTrackerTests
{
    [Fact]
    public void FirstStatus_ReturnsChanged()
    {
        var tracker = new StatusTracker();

        var (changed, previous) = tracker.TryGetStatusChange("target-1", "checker-1", MonitoringStatus.Healthy);

        Assert.True(changed);
        Assert.Equal(MonitoringStatus.Unknown, previous);
    }

    [Fact]
    public void SameStatus_ReturnsNotChanged()
    {
        var tracker = new StatusTracker();
        tracker.TryGetStatusChange("target-1", "checker-1", MonitoringStatus.Healthy);

        var (changed, previous) = tracker.TryGetStatusChange("target-1", "checker-1", MonitoringStatus.Healthy);

        Assert.False(changed);
        Assert.Equal(MonitoringStatus.Healthy, previous);
    }

    [Fact]
    public void DifferentStatus_ReturnsChanged()
    {
        var tracker = new StatusTracker();
        tracker.TryGetStatusChange("target-1", "checker-1", MonitoringStatus.Healthy);

        var (changed, previous) = tracker.TryGetStatusChange("target-1", "checker-1", MonitoringStatus.Unhealthy);

        Assert.True(changed);
        Assert.Equal(MonitoringStatus.Healthy, previous);
    }

    [Fact]
    public void DifferentCheckers_IndependentTracking()
    {
        var tracker = new StatusTracker();
        tracker.TryGetStatusChange("target-1", "checker-A", MonitoringStatus.Healthy);
        tracker.TryGetStatusChange("target-1", "checker-B", MonitoringStatus.Unhealthy);

        var (changedA, previousA) = tracker.TryGetStatusChange("target-1", "checker-A", MonitoringStatus.Healthy);
        var (changedB, previousB) = tracker.TryGetStatusChange("target-1", "checker-B", MonitoringStatus.Unhealthy);

        Assert.False(changedA);
        Assert.Equal(MonitoringStatus.Healthy, previousA);
        Assert.False(changedB);
        Assert.Equal(MonitoringStatus.Unhealthy, previousB);
    }
}
