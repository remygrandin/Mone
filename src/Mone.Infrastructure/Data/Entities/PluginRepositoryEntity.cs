namespace Mone.Infrastructure.Data.Entities;

public sealed class PluginRepositoryEntity
{
    public Guid Id { get; set; }
    public required string Owner { get; set; }
    public required string Repo { get; set; }
    public string? Branch { get; set; }
    public required string DisplayName { get; set; }
    public bool Enabled { get; set; } = true;
    public string? ETag { get; set; }
    public DateTime? LastSyncedAt { get; set; }
    public string? LastSyncError { get; set; }
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;

    public ICollection<PluginManifestEntity> Manifests { get; set; } = [];
}
