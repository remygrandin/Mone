namespace Mone.Infrastructure.Data.Entities;

public sealed class NotificationConfigEntity
{
    public Guid Id { get; set; }
    public required string PluginId { get; set; }
    public string? ConfigJson { get; set; }
    public bool Enabled { get; set; } = true;
    public string? Scope { get; set; }
}
