namespace Mone.Infrastructure.Data.Entities;

public sealed class HostTagEntity
{
    public Guid HostId { get; set; }
    public HostEntity Host { get; set; } = null!;

    public Guid TagId { get; set; }
    public TagEntity Tag { get; set; } = null!;
}
