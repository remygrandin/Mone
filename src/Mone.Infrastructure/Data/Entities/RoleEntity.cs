namespace Mone.Infrastructure.Data.Entities;

public sealed class RoleEntity
{
    public Guid Id { get; set; }
    public required string Name { get; set; }
    public string? Description { get; set; }

    /// <summary>Built-in roles (e.g. SuperAdmin) cannot be deleted or have their permissions edited.</summary>
    public bool IsSystem { get; set; }

    public DateTimeOffset CreatedAt { get; set; }
    public DateTimeOffset UpdatedAt { get; set; }

    public ICollection<RolePermissionEntity> Permissions { get; set; } = [];
    public ICollection<UserRoleAssignmentEntity> Assignments { get; set; } = [];
}
