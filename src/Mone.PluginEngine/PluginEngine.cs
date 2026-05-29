using System.Collections.Concurrent;
using System.Runtime.CompilerServices;
using Mone.Contracts.Plugins;
using Microsoft.Extensions.Logging;

namespace Mone.PluginEngine;

public sealed class PluginEngine : IDisposable
{
    private readonly PluginRegistry _registry = new();
    private readonly ConcurrentDictionary<string, PluginLoader> _loaders = new(StringComparer.OrdinalIgnoreCase);
    private readonly ILogger<PluginEngine> _logger;
    private readonly bool _enableHotReload;
    private bool _disposed;

    public event EventHandler<PluginEventArgs>? PluginLoaded;
    public event EventHandler<PluginEventArgs>? PluginUnloaded;
    public event EventHandler<PluginEventArgs>? PluginReloaded;
    public event EventHandler<PluginLoadFailedEventArgs>? PluginLoadFailed;

    public PluginRegistry Registry => _registry;

    public IReadOnlyList<string> LoadedAssemblyPaths => [.. _loaders.Keys];

    public PluginEngine(ILogger<PluginEngine> logger, bool enableHotReload = true)
    {
        _logger = logger;
        _enableHotReload = enableHotReload;
    }

    [MethodImpl(MethodImplOptions.NoInlining)]
    public void LoadPluginsFromDirectory(string pluginDirectory)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);

        if (!Directory.Exists(pluginDirectory))
        {
            _logger.LogWarning("Plugin directory does not exist: {Directory}", pluginDirectory);
            return;
        }

        var dllFiles = Directory.GetFiles(pluginDirectory, "*.dll", SearchOption.AllDirectories);

        foreach (var dllPath in dllFiles)
        {
            try
            {
                LoadPluginAssembly(dllPath);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Failed to load plugin from {Path}", dllPath);
                PluginLoadFailed?.Invoke(this, new PluginLoadFailedEventArgs(dllPath, ex));
            }
        }
    }

    [MethodImpl(MethodImplOptions.NoInlining)]
    public void LoadPluginAssembly(string assemblyPath)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);

        var fullPath = Path.GetFullPath(assemblyPath);
        _logger.LogInformation("Loading plugin assembly: {Path}", fullPath);

        var loader = PluginLoader.Create(fullPath, _enableHotReload);

        if (_enableHotReload)
        {
            loader.Reloaded += OnPluginReloaded;
        }

        try
        {
            var plugins = loader.LoadPlugins();
            if (plugins.Count == 0)
            {
                _logger.LogWarning("No plugin types found in assembly: {Path}", fullPath);
                loader.Dispose();
                return;
            }

            _loaders[fullPath] = loader;

            foreach (var (plugin, metadata) in plugins)
            {
                _registry.TryRegister(metadata.PluginId, new PluginRegistration(plugin, metadata));
                _logger.LogInformation("Loaded plugin {PluginId} ({Kind}) from {Path}",
                    metadata.PluginId, metadata.Kind, fullPath);
                PluginLoaded?.Invoke(this, new PluginEventArgs(metadata));
            }
        }
        catch (Exception ex)
        {
            loader.Dispose();
            _logger.LogError(ex, "Failed to load plugins from assembly: {Path}", fullPath);
            PluginLoadFailed?.Invoke(this, new PluginLoadFailedEventArgs(fullPath, ex));
            throw;
        }
    }

    public IReadOnlyList<T> GetPlugins<T>() where T : IPlugin
        => _registry.GetPlugins<T>();

    public IReadOnlyList<PluginRegistration> GetPluginsByKind(PluginKind kind)
        => _registry.GetByKind(kind);

    [MethodImpl(MethodImplOptions.NoInlining)]
    public void ReloadPlugin(string assemblyPath)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);

        var fullPath = Path.GetFullPath(assemblyPath);

        if (!_loaders.TryRemove(fullPath, out var oldLoader))
        {
            _logger.LogWarning("No loaded plugin found at path: {Path}", fullPath);
            return;
        }

        var oldRegistrations = _registry.GetAll()
            .Where(r => string.Equals(r.Metadata.AssemblyPath, fullPath, StringComparison.OrdinalIgnoreCase))
            .ToList();

        foreach (var reg in oldRegistrations)
            _registry.TryRemove(reg.Metadata.PluginId);

        oldLoader.Dispose();

        try
        {
            LoadPluginAssembly(fullPath);

            foreach (var reg in _registry.GetAll()
                .Where(r => string.Equals(r.Metadata.AssemblyPath, fullPath, StringComparison.OrdinalIgnoreCase)))
            {
                PluginReloaded?.Invoke(this, new PluginEventArgs(reg.Metadata));
            }
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to reload plugin from {Path}", fullPath);
            PluginLoadFailed?.Invoke(this, new PluginLoadFailedEventArgs(fullPath, ex));
        }
    }

    [MethodImpl(MethodImplOptions.NoInlining)]
    public void UnloadPlugin(string assemblyPath)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);

        var fullPath = Path.GetFullPath(assemblyPath);

        if (!_loaders.TryRemove(fullPath, out var loader))
        {
            _logger.LogWarning("No loaded plugin found at path: {Path}", fullPath);
            return;
        }

        var registrations = _registry.GetAll()
            .Where(r => string.Equals(r.Metadata.AssemblyPath, fullPath, StringComparison.OrdinalIgnoreCase))
            .ToList();

        foreach (var reg in registrations)
        {
            _registry.TryRemove(reg.Metadata.PluginId);
            _logger.LogInformation("Unloaded plugin {PluginId}", reg.Metadata.PluginId);
            PluginUnloaded?.Invoke(this, new PluginEventArgs(reg.Metadata));
        }

        loader.Dispose();
    }

    [MethodImpl(MethodImplOptions.NoInlining)]
    private void OnPluginReloaded(string assemblyPath)
    {
        _logger.LogInformation("Hot-reload detected for {Path}, reloading plugins", assemblyPath);
        try
        {
            ReloadPlugin(assemblyPath);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Hot-reload failed for {Path}", assemblyPath);
        }
    }

    [MethodImpl(MethodImplOptions.NoInlining)]
    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;

        foreach (var loader in _loaders.Values)
            loader.Dispose();

        _loaders.Clear();
    }
}

public sealed class PluginEventArgs(PluginMetadata metadata) : EventArgs
{
    public PluginMetadata Metadata { get; } = metadata;
}

public sealed class PluginLoadFailedEventArgs(string assemblyPath, Exception exception) : EventArgs
{
    public string AssemblyPath { get; } = assemblyPath;
    public Exception Exception { get; } = exception;
}
