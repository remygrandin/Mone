using Mone.Contracts.Models;

namespace Mone.Contracts.Plugins;

public interface IPlugin
{
    string Name { get; }
    Version Version { get; }
    string Description { get; }
    Task InitializeAsync(IPluginContext context);
}
