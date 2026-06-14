using Microsoft.EntityFrameworkCore;
using Mone.Api.Authorization;
using Mone.Api.Models;
using Mone.Contracts.Models;
using Mone.Infrastructure.Data;
using Mone.Infrastructure.Data.Entities;

namespace Mone.Api.Endpoints;

public static class UserAdminEndpoints
{
    public static void MapUserAdminEndpoints(this WebApplication app)
    {
        var group = app.MapGroup("/api/users")
            .WithTags("Users")
            .RequireAuthorization()
            .RequirePermission(PermissionResource.Administration);

        group.MapGet("/", async (MoneDbContext db) =>
        {
            var users = await db.Users
                .AsNoTracking()
                .OrderBy(u => u.Email)
                .Select(u => new { u.Id, u.Email })
                .ToListAsync();

            var assignments = await db.UserRoleAssignments
                .AsNoTracking()
                .Join(db.MoneRoles, a => a.RoleId, r => r.Id, (a, r) => new { a, RoleName = r.Name })
                .ToListAsync();

            var groupNames = await db.HostGroups.AsNoTracking()
                .ToDictionaryAsync(g => g.Id, g => g.Name);
            var tagNames = await db.Tags.AsNoTracking()
                .ToDictionaryAsync(t => t.Id, t => t.Name);

            var byUser = assignments
                .GroupBy(x => x.a.UserId)
                .ToDictionary(g => g.Key, g => g.ToList());

            var response = users.Select(u =>
            {
                var list = byUser.TryGetValue(u.Id, out var rows)
                    ? rows.Select(x => new UserRoleAssignmentResponse(
                        x.a.Id, x.a.RoleId, x.RoleName, x.a.ScopeType, x.a.ScopeId,
                        ResolveScopeName(x.a.ScopeType, x.a.ScopeId, groupNames, tagNames),
                        x.a.CreatedAt)).ToList()
                    : [];
                return new UserWithRolesResponse(u.Id, u.Email ?? "", list);
            }).ToList();

            return Results.Ok(response);
        })
        .WithName("ListUsers")
        .WithSummary("List all users with their role assignments and resolved scope names.")
        .Produces<IEnumerable<UserWithRolesResponse>>();

        group.MapPost("/{id}/roles", async (string id, AssignRoleRequest request, MoneDbContext db) =>
        {
            if (!await db.Users.AnyAsync(u => u.Id == id))
                return Results.Problem("User not found.", statusCode: StatusCodes.Status404NotFound);

            var role = await db.MoneRoles.FirstOrDefaultAsync(r => r.Id == request.RoleId);
            if (role is null) return Results.ValidationProblem(new Dictionary<string, string[]> { ["RoleId"] = new[] { "Role not found." } });

            if (request.ScopeType == ScopeType.Global)
            {
                if (request.ScopeId is not null)
                    return Results.ValidationProblem(new Dictionary<string, string[]> { ["ScopeId"] = new[] { "Global scope must not specify a scope id." } });
            }
            else
            {
                if (request.ScopeId is not { } scopeId)
                    return Results.ValidationProblem(new Dictionary<string, string[]> { ["ScopeId"] = new[] { "A scope id is required for Tag and Group scopes." } });

                var scopeExists = request.ScopeType == ScopeType.Group
                    ? await db.HostGroups.AnyAsync(g => g.Id == scopeId)
                    : await db.Tags.AnyAsync(t => t.Id == scopeId);
                if (!scopeExists)
                    return Results.ValidationProblem(new Dictionary<string, string[]> { ["ScopeId"] = new[] { $"{request.ScopeType} scope target not found." } });
            }

            var duplicate = await db.UserRoleAssignments.AnyAsync(a =>
                a.UserId == id && a.RoleId == request.RoleId &&
                a.ScopeType == request.ScopeType && a.ScopeId == request.ScopeId);
            if (duplicate)
                return Results.Problem("This role is already assigned to the user at this scope.", statusCode: StatusCodes.Status409Conflict);

            var assignment = new UserRoleAssignmentEntity
            {
                Id = Guid.NewGuid(),
                UserId = id,
                RoleId = request.RoleId,
                ScopeType = request.ScopeType,
                ScopeId = request.ScopeId,
                CreatedAt = DateTimeOffset.UtcNow
            };
            db.UserRoleAssignments.Add(assignment);
            await db.SaveChangesAsync();

            return Results.Created($"/api/users/{id}/roles/{assignment.Id}", new UserRoleAssignmentResponse(
                assignment.Id, role.Id, role.Name, assignment.ScopeType, assignment.ScopeId,
                null, assignment.CreatedAt));
        })
        .WithName("AssignUserRole")
        .WithSummary("Assign a role to a user at a Global, Group, or Tag scope. Returns 409 if the assignment already exists.")
        .Produces<UserRoleAssignmentResponse>(StatusCodes.Status201Created)
        .ProducesValidationProblem()
        .ProducesProblem(StatusCodes.Status404NotFound)
        .ProducesProblem(StatusCodes.Status409Conflict);

        group.MapDelete("/{id}/roles/{assignmentId:guid}", async (string id, Guid assignmentId, MoneDbContext db) =>
        {
            var assignment = await db.UserRoleAssignments
                .FirstOrDefaultAsync(a => a.Id == assignmentId && a.UserId == id);
            if (assignment is null) return Results.Problem("Role assignment not found.", statusCode: StatusCodes.Status404NotFound);

            if (await IsLastGlobalAdminAssignmentAsync(db, assignment))
                return Results.Problem(
                    "Cannot revoke the last Global assignment that grants Administration: Manage. " +
                    "Assign it to another user first to avoid locking yourself out.",
                    statusCode: StatusCodes.Status409Conflict);

            db.UserRoleAssignments.Remove(assignment);
            await db.SaveChangesAsync();
            return Results.NoContent();
        })
        .WithName("RevokeUserRole")
        .WithSummary("Revoke a user's role assignment. Returns 404 if not found, 409 if it is the last Global admin grant.")
        .Produces(StatusCodes.Status204NoContent)
        .ProducesProblem(StatusCodes.Status404NotFound)
        .ProducesProblem(StatusCodes.Status409Conflict);
    }

    /// <summary>
    /// True when the assignment is the only remaining Global-scoped grant of Administration:Manage
    /// across all users. Removing it would leave nobody able to manage roles/users.
    /// </summary>
    private static async Task<bool> IsLastGlobalAdminAssignmentAsync(
        MoneDbContext db, UserRoleAssignmentEntity assignment)
    {
        var adminRoleIds = await db.RolePermissions
            .Where(p => p.Resource == PermissionResource.Administration && p.Level == PermissionLevel.Manage)
            .Select(p => p.RoleId)
            .Distinct()
            .ToListAsync();

        if (assignment.ScopeType != ScopeType.Global || !adminRoleIds.Contains(assignment.RoleId))
            return false;

        var remaining = await db.UserRoleAssignments.CountAsync(a =>
            a.Id != assignment.Id &&
            a.ScopeType == ScopeType.Global &&
            adminRoleIds.Contains(a.RoleId));

        return remaining == 0;
    }

    private static string? ResolveScopeName(
        ScopeType type, Guid? scopeId,
        IReadOnlyDictionary<Guid, string> groupNames,
        IReadOnlyDictionary<Guid, string> tagNames)
    {
        if (scopeId is not { } id) return null;
        return type switch
        {
            ScopeType.Group => groupNames.TryGetValue(id, out var g) ? g : null,
            ScopeType.Tag => tagNames.TryGetValue(id, out var t) ? t : null,
            _ => null
        };
    }
}
