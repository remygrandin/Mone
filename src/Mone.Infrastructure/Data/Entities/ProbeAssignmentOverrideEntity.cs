namespace Mone.Infrastructure.Data.Entities;

public sealed class ProbeAssignmentOverrideEntity
{
    public Guid Id { get; set; }
    public Guid HostId { get; set; }
    public HostEntity Host { get; set; } = null!;
    public Guid ProbeAssignmentId { get; set; }
    public ProbeAssignmentEntity ProbeAssignment { get; set; } = null!;
    public string? ConfigJsonOverride { get; set; }
    public bool IsDisabled { get; set; }
    public DateTime CreatedAt { get; set; }
    public DateTime UpdatedAt { get; set; }
}
