namespace Mone.Contracts.Models;

public interface IPluginContext
{
    string PluginId { get; }
    IReadOnlyDictionary<string, string> Configuration { get; }
    CancellationToken ShutdownToken { get; }
}
