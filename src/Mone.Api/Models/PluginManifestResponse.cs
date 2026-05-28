namespace Mone.Api.Models;

public sealed record PluginManifestResponse(
    Guid Id,
    Guid RepositoryId,
    string Name,
    string Version,
    string? Description,
    string PluginType,
    string? Author,
    string? License,
    string? Homepage,
    bool IsInstalled,
    DateTime? InstalledAt,
    DateTime SyncedAt);
