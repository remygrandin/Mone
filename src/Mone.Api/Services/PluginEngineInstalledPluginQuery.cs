using Mone.Infrastructure.Services;

namespace Mone.Api.Services;

public sealed class PluginEngineInstalledPluginQuery(Mone.PluginEngine.PluginEngine engine) : IInstalledPluginQuery
{
    public IReadOnlyList<InstalledPluginInfo> List() =>
        engine.Registry.GetAll()
            .Select(r => new InstalledPluginInfo(r.Metadata.Name, r.Metadata.Version.ToString()))
            .ToList();
}
