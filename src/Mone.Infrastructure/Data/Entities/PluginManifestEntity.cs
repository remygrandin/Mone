namespace Mone.Infrastructure.Data.Entities;

public sealed class PluginManifestEntity
{
    public Guid Id { get; set; }
    public Guid RepositoryId { get; set; }
    public required string Name { get; set; }
    public required string Version { get; set; }
    public string? Description { get; set; }
    public required string PluginType { get; set; }
    public required string DownloadUrl { get; set; }
    public required string Sha256 { get; set; }
    public long? FileSize { get; set; }
    public string? DependenciesJson { get; set; }
    public string? MinMoneVersion { get; set; }
    public string? Author { get; set; }
    public string? License { get; set; }
    public string? Homepage { get; set; }
    public string? TagsJson { get; set; }
    public DateTime SyncedAt { get; set; }

    public PluginRepositoryEntity Repository { get; set; } = null!;
}
