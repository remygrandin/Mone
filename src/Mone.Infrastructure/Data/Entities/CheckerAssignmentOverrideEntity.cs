namespace Mone.Infrastructure.Data.Entities;

public sealed class CheckerAssignmentOverrideEntity
{
    public Guid Id { get; set; }
    public Guid HostId { get; set; }
    public HostEntity Host { get; set; } = null!;
    public Guid CheckerAssignmentId { get; set; }
    public CheckerAssignmentEntity CheckerAssignment { get; set; } = null!;
    public string? ConfigJsonOverride { get; set; }
    public bool IsDisabled { get; set; }
    public DateTime CreatedAt { get; set; }
    public DateTime UpdatedAt { get; set; }
}
