namespace Mone.Infrastructure.Data.Entities;

public sealed class GroupEntity
{
    public Guid Id { get; set; }
    public required string Name { get; set; }
    public string? Description { get; set; }
    public Guid? ParentGroupId { get; set; }
    public DateTimeOffset CreatedAt { get; set; }
    public DateTimeOffset UpdatedAt { get; set; }

    public GroupEntity? ParentGroup { get; set; }
    public ICollection<GroupEntity> ChildGroups { get; set; } = [];
    public ICollection<GroupMembershipEntity> GroupMemberships { get; set; } = [];
    public ICollection<ProbeAssignmentEntity> ProbeAssignments { get; set; } = [];
    public ICollection<CheckerAssignmentEntity> CheckerAssignments { get; set; } = [];
}
