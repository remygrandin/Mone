using Microsoft.EntityFrameworkCore;
using Mone.Contracts.Models;
using Mone.Infrastructure.Data;
using Mone.Infrastructure.Data.Entities;

namespace Mone.Api.Services;

/// <summary>
/// Idempotent seeding for the built-in SuperAdmin role and lockout-safety helpers.
/// SuperAdmin is a system role (cannot be edited/deleted via IAM) holding Manage on
/// every resource. A Global-scoped assignment of it is what grants unrestricted access.
/// </summary>
public static class RbacSeeder
{
    public const string SuperAdminRoleName = "SuperAdmin";

    /// <summary>
    /// Ensures the SuperAdmin role exists with a Manage grant on every PermissionResource.
    /// Reconciles missing permission rows on every call so newly-added resources are covered.
    /// Returns the role id.
    /// </summary>
    public static async Task<Guid> EnsureSuperAdminRoleAsync(MoneDbContext db, CancellationToken ct = default)
    {
        var role = await db.MoneRoles
            .Include(r => r.Permissions)
            .FirstOrDefaultAsync(r => r.Name == SuperAdminRoleName, ct);

        var now = DateTimeOffset.UtcNow;
        if (role is null)
        {
            role = new RoleEntity
            {
                Id = Guid.NewGuid(),
                Name = SuperAdminRoleName,
                Description = "Built-in role with full Manage access to every resource.",
                IsSystem = true,
                CreatedAt = now,
                UpdatedAt = now
            };
            db.MoneRoles.Add(role);
        }
        else if (!role.IsSystem)
        {
            role.IsSystem = true;
        }

        var existing = role.Permissions.ToDictionary(p => p.Resource);
        foreach (var resource in Enum.GetValues<PermissionResource>())
        {
            if (existing.TryGetValue(resource, out var perm))
            {
                if (perm.Level != PermissionLevel.Manage)
                    perm.Level = PermissionLevel.Manage;
            }
            else
            {
                role.Permissions.Add(new RolePermissionEntity
                {
                    RoleId = role.Id,
                    Resource = resource,
                    Level = PermissionLevel.Manage
                });
            }
        }

        await db.SaveChangesAsync(ct);
        return role.Id;
    }

    /// <summary>
    /// Ensures the given user holds a Global-scoped assignment of the given role. Idempotent.
    /// </summary>
    public static async Task EnsureGlobalAssignmentAsync(
        MoneDbContext db, string userId, Guid roleId, CancellationToken ct = default)
    {
        var exists = await db.UserRoleAssignments.AnyAsync(
            a => a.UserId == userId
                 && a.RoleId == roleId
                 && a.ScopeType == ScopeType.Global,
            ct);

        if (exists) return;

        db.UserRoleAssignments.Add(new UserRoleAssignmentEntity
        {
            Id = Guid.NewGuid(),
            UserId = userId,
            RoleId = roleId,
            ScopeType = ScopeType.Global,
            ScopeId = null,
            CreatedAt = DateTimeOffset.UtcNow
        });

        await db.SaveChangesAsync(ct);
    }
}
