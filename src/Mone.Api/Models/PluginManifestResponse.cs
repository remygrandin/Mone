namespace Mone.Api.Models;

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

public sealed record UninstallPluginRequest(string Name);
