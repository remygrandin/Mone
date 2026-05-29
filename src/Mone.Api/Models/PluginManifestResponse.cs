namespace Mone.Api.Models;

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

public sealed record UninstallPluginRequest(string Name);
