using Mone.Dashboard.Helpers;
using Xunit;

namespace Mone.Dashboard.Tests;

// Proves the probe-table row projection (S06): name resolution, last-run formatting, and the
// passive-mode predicate. Browserless: asserts directly over ProbeRowHelper, the chokepoint the
// markup calls, so render decisions are covered without bUnit. A fixed `now` keeps timing
// deterministic.
public class ProbeRowHelperTests
{
    [Fact]
    public void ResolveDisplayName_CustomNameWins()
    {
        Assert.Equal("My Ping", ProbeRowHelper.ResolveDisplayName("My Ping", "PingProbe@1.0.0"));
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    public void ResolveDisplayName_FallsBackToPluginName_WhenCustomNameMissing(string? customName)
    {
        Assert.Equal("PingProbe", ProbeRowHelper.ResolveDisplayName(customName, "PingProbe@1.0.0"));
    }

    [Fact]
    public void ResolveDisplayName_ReturnsIdUnchanged_WhenNoVersionSuffix()
    {
        Assert.Equal("PingProbe", ProbeRowHelper.ResolveDisplayName(null, "PingProbe"));
    }

    [Fact]
    public void FormatLastRun_ReturnsNever_WhenNull()
    {
        var now = new DateTimeOffset(2026, 6, 18, 12, 0, 0, TimeSpan.Zero);
        Assert.Equal("Never", ProbeRowHelper.FormatLastRun(null, now));
    }

    [Theory]
    [InlineData(30, "just now")]
    [InlineData(5 * 60, "5m ago")]
    [InlineData(3 * 60 * 60, "3h ago")]
    [InlineData(2 * 24 * 60 * 60, "2d ago")]
    public void FormatLastRun_BucketsElapsedTime(int elapsedSeconds, string expected)
    {
        var now = new DateTimeOffset(2026, 6, 18, 12, 0, 0, TimeSpan.Zero);
        var lastRun = now - TimeSpan.FromSeconds(elapsedSeconds);
        Assert.Equal(expected, ProbeRowHelper.FormatLastRun(lastRun, now));
    }

    [Theory]
    [InlineData("Passive", true)]
    [InlineData("passive", true)]
    [InlineData("Active", false)]
    [InlineData(null, false)]
    public void IsPassiveMode_MatchesPassiveCaseInsensitively(string? probeMode, bool expected)
    {
        Assert.Equal(expected, ProbeRowHelper.IsPassiveMode(probeMode));
    }

    [Fact]
    public void FormatTriggerToast_UsesCustomName_WhenPresent()
    {
        Assert.Equal("Probe 'My Ping' triggered.", ProbeRowHelper.FormatTriggerToast("My Ping", "PingProbe@1.0.0"));
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    public void FormatTriggerToast_FallsBackToPluginName_WhenCustomNameMissing(string? customName)
    {
        Assert.Equal("Probe 'PingProbe' triggered.", ProbeRowHelper.FormatTriggerToast(customName, "PingProbe@1.0.0"));
    }

    [Fact]
    public void FormatTriggerToast_UsesIdUnchanged_WhenNoVersionSuffix()
    {
        Assert.Equal("Probe 'PingProbe' triggered.", ProbeRowHelper.FormatTriggerToast(null, "PingProbe"));
    }
}
