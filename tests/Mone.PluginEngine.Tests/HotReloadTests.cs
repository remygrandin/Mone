using Mone.Contracts.Models;
using Mone.Contracts.Plugins;
using Microsoft.Extensions.Logging.Abstractions;
using Xunit;
using Xunit.Abstractions;

namespace Mone.PluginEngine.Tests;

public class HotReloadTests : IDisposable
{
    private readonly PluginEngine _engine;
    private readonly string _tempDir;
    private readonly ITestOutputHelper _output;

    public HotReloadTests(ITestOutputHelper output)
    {
        _output = output;
        _engine = new PluginEngine(NullLogger<PluginEngine>.Instance, enableHotReload: true);
        _tempDir = Path.Combine(Path.GetTempPath(), $"mone-hotreload-{Guid.NewGuid():N}");
        Directory.CreateDirectory(_tempDir);

        _engine.PluginLoadFailed += (_, args) =>
            _output.WriteLine($"LOAD FAILED: {args.AssemblyPath} — {args.Exception}");
    }

    [Fact]
    public async Task ReloadPlugin_SwapsImplementation_NewBehaviorObserved()
    {
        var v1Dll = TestPluginBuilder.BuildPlugin("TestProbePlugin");
        var pluginDir = Path.Combine(_tempDir, "probe");
        var targetDll = TestPluginBuilder.CopyPluginTo(v1Dll, pluginDir);

        _engine.LoadPluginAssembly(targetDll);

        var probeV1 = _engine.GetPlugins<IProbePlugin>().Single();
        var resultV1 = await probeV1.ExecuteAsync("host-1", CancellationToken.None);
        Assert.Equal(MonitoringStatus.Healthy, resultV1.Status);
        Assert.Contains("Ping OK", resultV1.Summary);

        var v2Dll = TestPluginBuilder.BuildPlugin("TestProbePluginV2");
        TestPluginBuilder.CopyPluginTo(v2Dll, pluginDir);

        _engine.ReloadPlugin(targetDll);

        var probeV2 = _engine.GetPlugins<IProbePlugin>().Single();
        var resultV2 = await probeV2.ExecuteAsync("host-1", CancellationToken.None);
        Assert.Equal(MonitoringStatus.Degraded, resultV2.Status);
        Assert.Contains("v2", resultV2.Summary);
    }

    [Fact]
    public void ReloadPlugin_RaisesReloadedEvent()
    {
        var v1Dll = TestPluginBuilder.BuildPlugin("TestProbePlugin");
        var pluginDir = Path.Combine(_tempDir, "reload-event");
        var targetDll = TestPluginBuilder.CopyPluginTo(v1Dll, pluginDir);

        _engine.LoadPluginAssembly(targetDll);

        PluginMetadata? reloadedMetadata = null;
        _engine.PluginReloaded += (_, args) => reloadedMetadata = args.Metadata;

        var v2Dll = TestPluginBuilder.BuildPlugin("TestProbePluginV2");
        TestPluginBuilder.CopyPluginTo(v2Dll, pluginDir);

        _engine.ReloadPlugin(targetDll);

        Assert.NotNull(reloadedMetadata);
        Assert.Equal(new Version(2, 0, 0), reloadedMetadata.Version);
    }

    [Fact]
    public void ReloadPlugin_RegistryCountRemainsOne()
    {
        var v1Dll = TestPluginBuilder.BuildPlugin("TestProbePlugin");
        var pluginDir = Path.Combine(_tempDir, "registry-count");
        var targetDll = TestPluginBuilder.CopyPluginTo(v1Dll, pluginDir);

        _engine.LoadPluginAssembly(targetDll);
        Assert.Equal(1, _engine.Registry.Count);

        var v2Dll = TestPluginBuilder.BuildPlugin("TestProbePluginV2");
        TestPluginBuilder.CopyPluginTo(v2Dll, pluginDir);
        _engine.ReloadPlugin(targetDll);

        Assert.Equal(1, _engine.Registry.Count);
    }

    public void Dispose()
    {
        _engine.Dispose();
        try { Directory.Delete(_tempDir, true); } catch { }
    }
}
