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

public sealed record PluginVersionResponse(
    Guid Id,
    string Version,
    string ReleaseTag,
    DateTime PublishedAt,
    bool IsPrerelease,
    string Sha256,
    long? FileSize);

public sealed record PluginCatalogResponse(
    string Name,
    string? Description,
    string PluginType,
    string? Author,
    string? License,
    string? Homepage,
    PluginStatus Status,
    Guid? RepositoryId,
    string? LatestVersion,
    string? LatestStableVersion,
    string? InstalledVersion,
    DateTime? SyncedAt,
    IReadOnlyList<PluginVersionResponse> Versions);

public sealed record InstallPluginRequest(Guid VersionId);

public sealed record UninstallPluginRequest(string Name);
