namespace Mone.Infrastructure.Data.Entities;

public sealed class GroupMembershipEntity
{
    public Guid GroupId { get; set; }
    public Guid HostId { get; set; }

    public GroupEntity Group { get; set; } = null!;
    public HostEntity Host { get; set; } = null!;
}
