using Mone.Contracts.Models;
using Mone.Contracts.Plugins;
using Microsoft.Extensions.Logging.Abstractions;
using Xunit;

namespace Mone.PluginEngine.Tests;

public class PluginLoadingTests : IDisposable
{
    private readonly PluginEngine _engine;

    public PluginLoadingTests()
    {
        _engine = new PluginEngine(NullLogger<PluginEngine>.Instance, enableHotReload: false);
    }

    [Fact]
    public async Task LoadProbePlugin_ExecuteAsync_ReturnsExpectedProbeResult()
    {
        var dllPath = TestPluginBuilder.BuildPlugin("TestProbePlugin");
        _engine.LoadPluginAssembly(dllPath);

        var probes = _engine.GetPlugins<IProbePlugin>();
        Assert.Single(probes);

        var probe = probes[0];
        Assert.Equal("PingProbe", probe.Name);
        Assert.Equal(new Version(1, 0, 0), probe.Version);

        var result = await probe.ExecuteAsync("target-1", CancellationToken.None);
        Assert.Equal(MonitoringStatus.Healthy, result.Status);
        Assert.Contains("target-1", result.Summary);
    }

    [Fact]
    public void LoadProbePlugin_RaisesPluginLoadedEvent()
    {
        var dllPath = TestPluginBuilder.BuildPlugin("TestProbePlugin");
        PluginMetadata? loadedMetadata = null;
        _engine.PluginLoaded += (_, args) => loadedMetadata = args.Metadata;

        _engine.LoadPluginAssembly(dllPath);

        Assert.NotNull(loadedMetadata);
        Assert.Equal("PingProbe", loadedMetadata.Name);
        Assert.Equal(PluginKind.Probe, loadedMetadata.Kind);
    }

    [Fact]
    public void LoadInvalidPath_RaisesPluginLoadFailedEvent()
    {
        string? failedPath = null;
        _engine.PluginLoadFailed += (_, args) => failedPath = args.AssemblyPath;

        Assert.ThrowsAny<Exception>(() => _engine.LoadPluginAssembly("/nonexistent/plugin.dll"));
    }

    [Fact]
    public void LoadCheckerPlugin_MetadataIsCorrect()
    {
        var dllPath = TestPluginBuilder.BuildPlugin("TestCheckerPlugin");
        _engine.LoadPluginAssembly(dllPath);

        var registration = _engine.Registry.GetAll().Single();
        Assert.Equal(PluginKind.Checker, registration.Metadata.Kind);
        Assert.Equal(CheckerInvocationMode.OnProbeResult, registration.Metadata.InvocationMode);
    }

    [Fact]
    public async Task LoadNotificationPlugin_SendAsync_ReturnsSuccess()
    {
        var dllPath = TestPluginBuilder.BuildPlugin("TestNotificationPlugin");
        _engine.LoadPluginAssembly(dllPath);

        var notifiers = _engine.GetPlugins<INotificationPlugin>();
        Assert.Single(notifiers);

        var change = new StatusChange(
            "target-1",
            MonitoringStatus.Unknown,
            MonitoringStatus.Healthy,
            new ProbeResult(MonitoringStatus.Healthy, "ok", DateTimeOffset.UtcNow, TimeSpan.Zero),
            DateTimeOffset.UtcNow);

        var result = await notifiers[0].SendAsync(change, CancellationToken.None);
        Assert.True(result.Success);
    }

    public void Dispose() => _engine.Dispose();
}
