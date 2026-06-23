namespace Mone.Infrastructure.Data.Entities;

public sealed class HostEntity
{
    public Guid Id { get; set; }
    public required string Name { get; set; }
    public required string Address { get; set; }
    public bool Enabled { get; set; } = true;
    public int ErrorPolicyThreshold { get; set; } = 1;
    public DateTimeOffset CreatedAt { get; set; }
    public DateTimeOffset UpdatedAt { get; set; }

    public ICollection<HostTagEntity> HostTags { get; set; } = [];
    public ICollection<ProbeAssignmentEntity> ProbeAssignments { get; set; } = [];
    public ICollection<CheckerAssignmentEntity> CheckerAssignments { get; set; } = [];
    public ICollection<GroupMembershipEntity> GroupMemberships { get; set; } = [];
    public ICollection<MaintenanceWindowEntity> MaintenanceWindows { get; set; } = [];
}
