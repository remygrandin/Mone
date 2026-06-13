using Mone.Contracts.Models;

namespace Mone.Infrastructure.Data.Entities;

public sealed class RolePermissionEntity
{
    public Guid RoleId { get; set; }
    public PermissionResource Resource { get; set; }
    public PermissionLevel Level { get; set; }

    public RoleEntity Role { get; set; } = null!;
}
