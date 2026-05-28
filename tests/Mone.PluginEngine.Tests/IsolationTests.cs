using Mone.Contracts.Models;
using Mone.Contracts.Plugins;
using Microsoft.Extensions.Logging.Abstractions;
using Xunit;

namespace Mone.PluginEngine.Tests;

public class IsolationTests : IDisposable
{
    private readonly PluginEngine _engine;

    public IsolationTests()
    {
        _engine = new PluginEngine(NullLogger<PluginEngine>.Instance, enableHotReload: false);
    }

    [Fact]
    public async Task LoadConflictingNewtonsoftVersions_BothProduceCorrectResults()
    {
        var v12Dll = TestPluginBuilder.BuildPlugin("TestPluginWithJsonV12");
        var v13Dll = TestPluginBuilder.BuildPlugin("TestPluginWithJsonV13");

        _engine.LoadPluginAssembly(v12Dll);
        _engine.LoadPluginAssembly(v13Dll);

        var probes = _engine.GetPlugins<IProbePlugin>();
        Assert.Equal(2, probes.Count);

        var v12Plugin = probes.Single(p => p.Name == "JsonV12Probe");
        var v13Plugin = probes.Single(p => p.Name == "JsonV13Probe");

        var v12Result = await v12Plugin.ExecuteAsync("iso-test", CancellationToken.None);
        var v13Result = await v13Plugin.ExecuteAsync("iso-test", CancellationToken.None);

        Assert.Equal(MonitoringStatus.Healthy, v12Result.Status);
        Assert.Equal(MonitoringStatus.Healthy, v13Result.Status);

        Assert.Contains("JsonV12", v12Result.Summary);
        Assert.Contains("JsonV13", v13Result.Summary);

        Assert.Contains("iso-test", v12Result.Summary);
        Assert.Contains("iso-test", v13Result.Summary);

        Assert.NotNull(v12Result.Metadata);
        Assert.NotNull(v13Result.Metadata);
        Assert.True(v12Result.Metadata.ContainsKey("newtonsoft_version"));
        Assert.True(v13Result.Metadata.ContainsKey("newtonsoft_version"));
    }

    [Fact]
    public void LoadConflictingVersions_PluginsAreInSeparateLoadContexts()
    {
        var v12Dll = TestPluginBuilder.BuildPlugin("TestPluginWithJsonV12");
        var v13Dll = TestPluginBuilder.BuildPlugin("TestPluginWithJsonV13");

        _engine.LoadPluginAssembly(v12Dll);
        _engine.LoadPluginAssembly(v13Dll);

        var registrations = _engine.Registry.GetAll();
        Assert.Equal(2, registrations.Count);

        var v12Reg = registrations.Single(r => r.Metadata.Name == "JsonV12Probe");
        var v13Reg = registrations.Single(r => r.Metadata.Name == "JsonV13Probe");

        Assert.NotEqual(v12Reg.Plugin.GetType().Assembly, v13Reg.Plugin.GetType().Assembly);
    }

    public void Dispose() => _engine.Dispose();
}
