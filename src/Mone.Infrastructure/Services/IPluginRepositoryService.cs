using Mone.Infrastructure.Data.Entities;

namespace Mone.Infrastructure.Services;

public interface IPluginRepositoryService
{
    Task SyncRepositoryAsync(Guid repoId, CancellationToken ct = default);
    Task InstallPluginAsync(Guid manifestId, CancellationToken ct = default);
    Task UninstallPluginAsync(Guid manifestId, CancellationToken ct = default);
    Task<IReadOnlyList<PluginManifestEntity>> GetAvailablePluginsAsync(CancellationToken ct = default);
    Task<IReadOnlyList<PluginManifestEntity>> GetInstalledPluginsAsync(CancellationToken ct = default);
}
