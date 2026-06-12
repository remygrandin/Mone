using System.Reflection;
using System.Runtime.CompilerServices;
using Mone.Contracts.Models;
using Mone.Contracts.Plugins;
using Mone.Contracts.Plugins.Attributes;
using McMaster.NETCore.Plugins;

namespace Mone.PluginEngine;

public sealed class PluginLoader : IDisposable
{
    private McMaster.NETCore.Plugins.PluginLoader? _loader;
    private readonly string _assemblyPath;

    public string AssemblyPath => _assemblyPath;

    private PluginLoader(string assemblyPath, McMaster.NETCore.Plugins.PluginLoader loader)
    {
        _assemblyPath = assemblyPath;
        _loader = loader;
    }

    public static PluginLoader Create(string assemblyPath, bool enableHotReload)
    {
        var loader = McMaster.NETCore.Plugins.PluginLoader.CreateFromAssemblyFile(
            assemblyPath,
            sharedTypes: [
                typeof(IPlugin),
                typeof(IProbePlugin),
                typeof(IPassiveProbePlugin),
                typeof(IPassiveProbeHost),
                typeof(PassiveAssignment),
                typeof(PassiveProtocol),
                typeof(ICheckerPlugin),
                typeof(INotificationPlugin),
                typeof(IPluginContext),
                typeof(ProbeResult),
                typeof(StatusChange),
                typeof(DeliveryResult),
                typeof(BackoffStrategy),
                typeof(MonitoringStatus),
                typeof(ProbeMode),
                typeof(InstantiationMode),
                typeof(CheckerInvocationMode),
                typeof(CheckerEvaluationContext),
                typeof(IMetricHistoryAccessor),
                typeof(ProbeResultRecord),
                typeof(ProbePluginAttribute),
                typeof(CheckerPluginAttribute),
                typeof(NotificationPluginAttribute),
                typeof(IConfigurablePlugin),
                typeof(ConfigManifest),
                typeof(ConfigField),
                typeof(ConfigFieldType),
                typeof(ConfigValidationRules),
            ],
            configure: config =>
            {
                config.PreferSharedTypes = true;
                config.IsUnloadable = true;
                config.EnableHotReload = enableHotReload;
                config.LoadInMemory = enableHotReload;
            });

        return new PluginLoader(assemblyPath, loader);
    }

    [MethodImpl(MethodImplOptions.NoInlining)]
    public IReadOnlyList<(IPlugin Plugin, PluginMetadata Metadata)> LoadPlugins()
    {
        if (_loader is null)
            throw new ObjectDisposedException(nameof(PluginLoader));

        var assembly = _loader.LoadDefaultAssembly();
        var results = new List<(IPlugin, PluginMetadata)>();

        foreach (var type in assembly.GetExportedTypes())
        {
            if (type.IsAbstract || type.IsInterface)
                continue;

            if (!typeof(IPlugin).IsAssignableFrom(type))
                continue;

            var plugin = (IPlugin)Activator.CreateInstance(type)!;
            var metadata = ExtractMetadata(type, plugin);
            if (metadata is not null)
                results.Add((plugin, metadata));
        }

        return results;
    }

    public event Action<string>? Reloaded
    {
        add
        {
            if (_loader is not null)
                _loader.Reloaded += (_, _) => value?.Invoke(_assemblyPath);
        }
        remove { }
    }

    [MethodImpl(MethodImplOptions.NoInlining)]
    private PluginMetadata? ExtractMetadata(Type type, IPlugin plugin)
    {
        var pluginId = $"{plugin.Name}@{plugin.Version}";
        var configManifest = ExtractConfigManifest(plugin);
        var infoVersion = ReadInformationalVersion(type.Assembly);

        // Passive probes are recognised by the interface alone — they own their listener and need
        // no ProbePluginAttribute. InstantiationMode is not a passive concept (the plugin is a
        // singleton that accumulates per-assignment config), so it is left null.
        if (typeof(IPassiveProbePlugin).IsAssignableFrom(type))
        {
            return new PluginMetadata
            {
                PluginId = pluginId,
                Name = plugin.Name,
                Version = plugin.Version,
                InformationalVersion = infoVersion,
                Description = plugin.Description,
                PluginTypeName = type.FullName!,
                Kind = PluginKind.Probe,
                ProbeMode = Mone.Contracts.Models.ProbeMode.Passive,
                InstantiationMode = null,
                AssemblyPath = _assemblyPath,
                ConfigManifest = configManifest
            };
        }

        if (typeof(IProbePlugin).IsAssignableFrom(type))
        {
            var attr = type.GetCustomAttribute<ProbePluginAttribute>();
            return new PluginMetadata
            {
                PluginId = pluginId,
                Name = plugin.Name,
                Version = plugin.Version,
                InformationalVersion = infoVersion,
                Description = plugin.Description,
                PluginTypeName = type.FullName!,
                Kind = PluginKind.Probe,
                ProbeMode = attr?.ProbeMode,
                InstantiationMode = attr?.InstantiationMode,
                AssemblyPath = _assemblyPath,
                ConfigManifest = configManifest
            };
        }

        if (typeof(ICheckerPlugin).IsAssignableFrom(type))
        {
            var attr = type.GetCustomAttribute<CheckerPluginAttribute>();
            var checker = plugin as ICheckerPlugin;
            return new PluginMetadata
            {
                PluginId = pluginId,
                Name = plugin.Name,
                Version = plugin.Version,
                InformationalVersion = infoVersion,
                Description = plugin.Description,
                PluginTypeName = type.FullName!,
                Kind = PluginKind.Checker,
                InvocationMode = attr?.InvocationMode ?? checker?.InvocationMode,
                Interval = checker?.Interval,
                AssemblyPath = _assemblyPath,
                ConfigManifest = configManifest
            };
        }

        if (typeof(INotificationPlugin).IsAssignableFrom(type))
        {
            return new PluginMetadata
            {
                PluginId = pluginId,
                Name = plugin.Name,
                Version = plugin.Version,
                InformationalVersion = infoVersion,
                Description = plugin.Description,
                PluginTypeName = type.FullName!,
                Kind = PluginKind.Notification,
                AssemblyPath = _assemblyPath,
                ConfigManifest = configManifest
            };
        }

        return null;
    }

    private static string? ReadInformationalVersion(Assembly assembly)
    {
        var raw = assembly.GetCustomAttribute<AssemblyInformationalVersionAttribute>()?.InformationalVersion;
        if (string.IsNullOrWhiteSpace(raw)) return null;
        var plus = raw.IndexOf('+');
        return plus >= 0 ? raw[..plus] : raw;
    }

    private static ConfigManifest? ExtractConfigManifest(IPlugin plugin)
    {
        if (plugin is not IConfigurablePlugin configurable)
            return null;

        try
        {
            return configurable.GetConfigManifest();
        }
        catch
        {
            return null;
        }
    }

    [MethodImpl(MethodImplOptions.NoInlining)]
    public void Dispose()
    {
        var loader = _loader;
        _loader = null;
        loader?.Dispose();
    }
}
