namespace Mone.Dashboard.Models;

public sealed record PluginRepositoryResponse(
    Guid Id,
    string Owner,
    string Repo,
    string? Branch,
    string DisplayName,
    bool Enabled,
    DateTime? LastSyncedAt,
    string? LastSyncError,
    DateTime CreatedAt);

public sealed record AddRepositoryRequest(
    string Owner,
    string Repo,
    string? Branch = null,
    string? DisplayName = null);

public enum PluginStatus { Available, Installed, UpdateAvailable }

public sealed record PluginCatalogResponse(
    string Name,
    string? Description,
    string PluginType,
    string? Author,
    string? License,
    string? Homepage,
    PluginStatus Status,
    Guid? ManifestId,
    Guid? RepositoryId,
    string? LatestVersion,
    string? InstalledVersion,
    DateTime? SyncedAt);

public sealed record InstallPluginRequest(Guid ManifestId);

public sealed record UninstallPluginRequest(string Name);
