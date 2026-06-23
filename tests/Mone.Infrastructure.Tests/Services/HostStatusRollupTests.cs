using Mone.Contracts.Models;
using Mone.Infrastructure.Services;
using Xunit;

namespace Mone.Infrastructure.Tests.Services;

public class HostStatusRollupTests
{
    [Fact]
    public void Empty_ReturnsUnknown()
    {
        var result = HostStatusRollup.ComputeResult([], errorThreshold: 1);

        Assert.Equal(MonitoringStatus.Unknown, result.Status);
        Assert.Equal(0, result.CheckerCount);
        Assert.Equal(0, result.ErrorCount);
        Assert.Equal(0, result.WarningCount);
    }

    [Fact]
    public void SingleUnhealthy_WithThresholdTwo_ReturnsDegraded()
    {
        var statuses = new[] { MonitoringStatus.Unhealthy };

        var result = HostStatusRollup.ComputeResult(statuses, errorThreshold: 2);

        Assert.Equal(MonitoringStatus.Degraded, result.Status);
        Assert.Equal(1, result.ErrorCount);
    }

    [Fact]
    public void TwoUnhealthy_WithThresholdTwo_ReturnsUnhealthy()
    {
        var statuses = new[] { MonitoringStatus.Unhealthy, MonitoringStatus.Unhealthy };

        var result = HostStatusRollup.ComputeResult(statuses, errorThreshold: 2);

        Assert.Equal(MonitoringStatus.Unhealthy, result.Status);
        Assert.Equal(2, result.ErrorCount);
        Assert.Equal(2, result.CheckerCount);
    }

    [Fact]
    public void SingleUnhealthy_WithThresholdOne_ReturnsUnhealthy()
    {
        var statuses = new[] { MonitoringStatus.Unhealthy };

        Assert.Equal(MonitoringStatus.Unhealthy, HostStatusRollup.Compute(statuses, errorThreshold: 1));
    }

    [Fact]
    public void DegradedAndHealthy_ReturnsDegraded()
    {
        var statuses = new[] { MonitoringStatus.Degraded, MonitoringStatus.Healthy };

        var result = HostStatusRollup.ComputeResult(statuses, errorThreshold: 1);

        Assert.Equal(MonitoringStatus.Degraded, result.Status);
        Assert.Equal(0, result.ErrorCount);
        Assert.Equal(1, result.WarningCount);
    }

    [Fact]
    public void AllHealthy_ReturnsHealthy()
    {
        var statuses = new[] { MonitoringStatus.Healthy, MonitoringStatus.Healthy };

        Assert.Equal(MonitoringStatus.Healthy, HostStatusRollup.Compute(statuses, errorThreshold: 1));
    }

    [Fact]
    public void SingleUnreachable_WithThresholdOne_ReturnsUnhealthy()
    {
        var statuses = new[] { MonitoringStatus.Unreachable };

        var result = HostStatusRollup.ComputeResult(statuses, errorThreshold: 1);

        Assert.Equal(MonitoringStatus.Unhealthy, result.Status);
        Assert.Equal(1, result.ErrorCount);
    }

    [Fact]
    public void UnknownAndHealthy_ReturnsHealthy()
    {
        var statuses = new[] { MonitoringStatus.Unknown, MonitoringStatus.Healthy };

        Assert.Equal(MonitoringStatus.Healthy, HostStatusRollup.Compute(statuses, errorThreshold: 1));
    }

    [Fact]
    public void ThresholdBelowOne_IsClampedToOne()
    {
        var statuses = new[] { MonitoringStatus.Unhealthy };

        // With threshold clamped to 1, a single error must roll up to Unhealthy.
        Assert.Equal(MonitoringStatus.Unhealthy, HostStatusRollup.Compute(statuses, errorThreshold: 0));
        Assert.Equal(MonitoringStatus.Unhealthy, HostStatusRollup.Compute(statuses, errorThreshold: -5));
    }

    [Fact]
    public void AllUnknown_ReturnsUnknown()
    {
        var statuses = new[] { MonitoringStatus.Unknown, MonitoringStatus.Unknown };

        Assert.Equal(MonitoringStatus.Unknown, HostStatusRollup.Compute(statuses, errorThreshold: 1));
    }
}
