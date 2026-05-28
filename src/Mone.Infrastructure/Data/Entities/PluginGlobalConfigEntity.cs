namespace Mone.Infrastructure.Data.Entities;

public sealed class PluginGlobalConfigEntity
{
    public Guid Id { get; set; }
    public required string PluginId { get; set; }
    public string? ConfigJson { get; set; }
    public DateTime UpdatedAt { get; set; }
}
