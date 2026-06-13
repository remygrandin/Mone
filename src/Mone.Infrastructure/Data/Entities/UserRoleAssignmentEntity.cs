using Mone.Contracts.Models;

namespace Mone.Infrastructure.Data.Entities;

/// <summary>
/// Binds a user to a role at a given scope. The scope (Global / Tag / Group) is chosen when the
/// role is granted, so a single reusable role can be applied with different breadth per user.
/// </summary>
public sealed class UserRoleAssignmentEntity
{
    public Guid Id { get; set; }
    public required string UserId { get; set; }
    public Guid RoleId { get; set; }
    public ScopeType ScopeType { get; set; }

    /// <summary>Tag id or Group id; null for Global scope.</summary>
    public Guid? ScopeId { get; set; }

    public DateTimeOffset CreatedAt { get; set; }

    public UserEntity User { get; set; } = null!;
    public RoleEntity Role { get; set; } = null!;
}
