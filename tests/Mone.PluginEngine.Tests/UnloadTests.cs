using System.Runtime.CompilerServices;
using Mone.Contracts.Plugins;
using Microsoft.Extensions.Logging.Abstractions;
using Xunit;

namespace Mone.PluginEngine.Tests;

public class UnloadTests : IDisposable
{
    private readonly PluginEngine _engine;

    public UnloadTests()
    {
        _engine = new PluginEngine(NullLogger<PluginEngine>.Instance, enableHotReload: false);
    }

    [Fact]
    public void UnloadPlugin_RemovesFromRegistry()
    {
        var dllPath = TestPluginBuilder.BuildPlugin("TestProbePlugin");
        _engine.LoadPluginAssembly(dllPath);
        Assert.Equal(1, _engine.Registry.Count);

        _engine.UnloadPlugin(dllPath);
        Assert.Equal(0, _engine.Registry.Count);
        Assert.Empty(_engine.GetPlugins<IProbePlugin>());
    }

    [Fact]
    public void UnloadPlugin_RaisesUnloadedEvent()
    {
        var dllPath = TestPluginBuilder.BuildPlugin("TestProbePlugin");
        _engine.LoadPluginAssembly(dllPath);

        PluginMetadata? unloadedMetadata = null;
        _engine.PluginUnloaded += (_, args) => unloadedMetadata = args.Metadata;

        _engine.UnloadPlugin(dllPath);

        Assert.NotNull(unloadedMetadata);
        Assert.Equal("PingProbe", unloadedMetadata.Name);
    }

    [Fact]
    [MethodImpl(MethodImplOptions.NoInlining)]
    public void UnloadPlugin_WeakReferenceGoesDeadAfterGC()
    {
        var dllPath = TestPluginBuilder.BuildPlugin("TestProbePlugin");
        var weakRef = LoadAndUnload(dllPath);

        for (var i = 0; i < 10; i++)
        {
            GC.Collect();
            GC.WaitForPendingFinalizers();
            GC.Collect();

            if (!weakRef.IsAlive)
                break;
        }

        Assert.False(weakRef.IsAlive, "Plugin instance should be collected after unload + GC");
    }

    [Fact]
    public void ReloadSamePath_ReplacesRegistrationInsteadOfKeepingStale()
    {
        var dllPath = TestPluginBuilder.BuildPlugin("TestProbePlugin");

        _engine.LoadPluginAssembly(dllPath);
        var first = _engine.GetPlugins<IProbePlugin>().Single();

        // An in-place update overwrites the same DLL path. The PluginId is
        // Name@Version (base version), identical across CI builds sharing a base
        // version, so a naive TryAdd would silently keep the stale registration
        // and leak the prior loader. Reloading the same path must replace it.
        _engine.LoadPluginAssembly(dllPath);
        var second = _engine.GetPlugins<IProbePlugin>().Single();

        Assert.Equal(1, _engine.Registry.Count);
        Assert.Single(_engine.LoadedAssemblyPaths);
        Assert.NotSame(first, second);
    }

    [MethodImpl(MethodImplOptions.NoInlining)]
    private WeakReference LoadAndUnload(string dllPath)
    {
        _engine.LoadPluginAssembly(dllPath);
        var plugin = _engine.GetPlugins<IProbePlugin>().Single();
        var weakRef = new WeakReference(plugin);

        plugin = null;
        _engine.UnloadPlugin(dllPath);

        return weakRef;
    }

    public void Dispose() => _engine.Dispose();
}
