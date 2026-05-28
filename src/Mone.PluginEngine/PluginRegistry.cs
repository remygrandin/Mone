using System.Collections.Concurrent;
using Mone.Contracts.Plugins;

namespace Mone.PluginEngine;

public sealed class PluginRegistry
{
    private readonly ConcurrentDictionary<string, PluginRegistration> _plugins = new(StringComparer.OrdinalIgnoreCase);

    public bool TryRegister(string pluginId, PluginRegistration registration)
        => _plugins.TryAdd(pluginId, registration);

    public bool TryRemove(string pluginId)
        => _plugins.TryRemove(pluginId, out _);

    public PluginRegistration? Get(string pluginId)
        => _plugins.GetValueOrDefault(pluginId);

    public bool Update(string pluginId, PluginRegistration registration)
        => _plugins.TryUpdate(pluginId, registration, _plugins.GetValueOrDefault(pluginId)!);

    public IReadOnlyList<PluginRegistration> GetAll()
        => [.. _plugins.Values];

    public IReadOnlyList<PluginRegistration> GetByKind(PluginKind kind)
        => [.. _plugins.Values.Where(r => r.Metadata.Kind == kind)];

    public IReadOnlyList<T> GetPlugins<T>() where T : IPlugin
        => [.. _plugins.Values.Where(r => r.Plugin is T).Select(r => (T)r.Plugin)];

    public int Count => _plugins.Count;

    public int CountByKind(PluginKind kind)
        => _plugins.Values.Count(r => r.Metadata.Kind == kind);
}

public sealed record PluginRegistration(IPlugin Plugin, PluginMetadata Metadata);
