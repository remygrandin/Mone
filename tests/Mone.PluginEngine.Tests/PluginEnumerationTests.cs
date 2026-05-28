using Mone.Contracts.Plugins;
using Microsoft.Extensions.Logging.Abstractions;
using Xunit;

namespace Mone.PluginEngine.Tests;

public class PluginEnumerationTests : IDisposable
{
    private readonly PluginEngine _engine;

    public PluginEnumerationTests()
    {
        _engine = new PluginEngine(NullLogger<PluginEngine>.Instance, enableHotReload: false);
    }

    [Fact]
    public void GetPlugins_FiltersByInterface_ReturnsCorrectSubset()
    {
        _engine.LoadPluginAssembly(TestPluginBuilder.BuildPlugin("TestProbePlugin"));
        _engine.LoadPluginAssembly(TestPluginBuilder.BuildPlugin("TestCheckerPlugin"));
        _engine.LoadPluginAssembly(TestPluginBuilder.BuildPlugin("TestNotificationPlugin"));

        var probes = _engine.GetPlugins<IProbePlugin>();
        var checkers = _engine.GetPlugins<ICheckerPlugin>();
        var notifiers = _engine.GetPlugins<INotificationPlugin>();

        Assert.Single(probes);
        Assert.Single(checkers);
        Assert.Single(notifiers);
        Assert.Equal(3, _engine.Registry.Count);
    }

    [Fact]
    public void GetPluginsByKind_ReturnsCorrectSubset()
    {
        _engine.LoadPluginAssembly(TestPluginBuilder.BuildPlugin("TestProbePlugin"));
        _engine.LoadPluginAssembly(TestPluginBuilder.BuildPlugin("TestCheckerPlugin"));
        _engine.LoadPluginAssembly(TestPluginBuilder.BuildPlugin("TestNotificationPlugin"));

        var probesByKind = _engine.GetPluginsByKind(PluginKind.Probe);
        var checkersByKind = _engine.GetPluginsByKind(PluginKind.Checker);
        var notifiersByKind = _engine.GetPluginsByKind(PluginKind.Notification);

        Assert.Single(probesByKind);
        Assert.Single(checkersByKind);
        Assert.Single(notifiersByKind);
    }

    [Fact]
    public void Registry_CountByKind_ReturnsCorrectCounts()
    {
        _engine.LoadPluginAssembly(TestPluginBuilder.BuildPlugin("TestProbePlugin"));
        _engine.LoadPluginAssembly(TestPluginBuilder.BuildPlugin("TestCheckerPlugin"));
        _engine.LoadPluginAssembly(TestPluginBuilder.BuildPlugin("TestNotificationPlugin"));

        Assert.Equal(1, _engine.Registry.CountByKind(PluginKind.Probe));
        Assert.Equal(1, _engine.Registry.CountByKind(PluginKind.Checker));
        Assert.Equal(1, _engine.Registry.CountByKind(PluginKind.Notification));
    }

    public void Dispose() => _engine.Dispose();
}
