using Mone.Contracts.Models;

namespace Mone.ProbeExecutor.Services;

public sealed class PluginContext(
    string pluginId,
    IReadOnlyDictionary<string, string> configuration,
    CancellationToken shutdownToken) : IPluginContext
{
    public string PluginId => pluginId;
    public IReadOnlyDictionary<string, string> Configuration => configuration;
    public CancellationToken ShutdownToken => shutdownToken;
}
