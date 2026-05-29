using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Mone.Messaging;
using Mone.Messaging.Messages;
using NATS.Client.Core;

namespace Mone.PluginEngine;

public sealed class PluginReloadListener : BackgroundService
{
    private readonly INatsConnection _nats;
    private readonly PluginEngine _engine;
    private readonly ILogger<PluginReloadListener> _logger;
    private readonly string _pluginsBaseDir;

    public PluginReloadListener(
        INatsConnection nats,
        PluginEngine engine,
        IConfiguration config,
        ILogger<PluginReloadListener> logger,
        string configKey = "PluginDirectory")
    {
        _nats = nats;
        _engine = engine;
        _logger = logger;
        _pluginsBaseDir = Path.GetFullPath(ResolvePluginDir(config, configKey));
    }

    private static string ResolvePluginDir(IConfiguration config, string configKey)
    {
        foreach (var section in new[] { "Api", "ProbeExecutor", "CheckerEngine", "AlertEngine" })
        {
            var value = config[$"{section}:{configKey}"];
            if (!string.IsNullOrWhiteSpace(value)) return value;
        }
        return "plugins";
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        _logger.LogInformation(
            "PluginReloadListener starting — Subject={Subject}, PluginsDir={PluginsDir}",
            MoneStreams.PluginReload.Subject, _pluginsBaseDir);

        await foreach (var msg in _nats.SubscribeAsync<PluginReloadMessage>(
            MoneStreams.PluginReload.Subject, cancellationToken: stoppingToken))
        {
            if (msg.Data is null)
            {
                _logger.LogWarning("PluginReloadListener received message with null payload");
                continue;
            }

            try
            {
                Handle(msg.Data);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex,
                    "PluginReloadListener failed to handle {Action} for {PluginName}",
                    msg.Data.Action, msg.Data.PluginName);
            }
        }
    }

    private void Handle(PluginReloadMessage message)
    {
        var pluginDir = Path.GetFullPath(Path.Combine(_pluginsBaseDir, message.PluginName));

        switch (message.Action)
        {
            case PluginReloadAction.Install:
                _logger.LogInformation(
                    "PluginReloadListener handling Install — PluginName={PluginName}, PluginDir={PluginDir}",
                    message.PluginName, pluginDir);
                if (Directory.Exists(pluginDir))
                {
                    _engine.LoadPluginsFromDirectory(pluginDir);
                }
                else
                {
                    _logger.LogWarning(
                        "PluginReloadListener Install missed — directory does not exist yet: {PluginDir}",
                        pluginDir);
                }
                break;

            case PluginReloadAction.Uninstall:
                _logger.LogInformation(
                    "PluginReloadListener handling Uninstall — PluginName={PluginName}, PluginDir={PluginDir}",
                    message.PluginName, pluginDir);
                var prefix = pluginDir + Path.DirectorySeparatorChar;
                var toUnload = _engine.LoadedAssemblyPaths
                    .Where(p => p.StartsWith(prefix, StringComparison.OrdinalIgnoreCase))
                    .ToList();
                foreach (var path in toUnload)
                    _engine.UnloadPlugin(path);
                break;
        }
    }
}
