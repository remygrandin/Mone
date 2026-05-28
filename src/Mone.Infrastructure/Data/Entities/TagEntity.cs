namespace Mone.Infrastructure.Data.Entities;

public sealed class TagEntity
{
    public Guid Id { get; set; }
    public required string Name { get; set; }

    public ICollection<HostTagEntity> HostTags { get; set; } = [];
}
