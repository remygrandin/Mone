using Mone.Infrastructure.Data.Entities;

namespace Mone.Infrastructure.Services;

public interface IPluginRepositoryService
{
    Task SyncRepositoryAsync(Guid repoId, CancellationToken ct = default);
    Task InstallPluginAsync(Guid manifestId, CancellationToken ct = default);
    Task UninstallPluginAsync(string pluginName, CancellationToken ct = default);
    Task<IReadOnlyList<PluginManifestEntity>> GetAvailablePluginsAsync(CancellationToken ct = default);
}

public interface IInstalledPluginQuery
{
    IReadOnlyList<InstalledPluginInfo> List();
}

public sealed record InstalledPluginInfo(string Name, string Version);
