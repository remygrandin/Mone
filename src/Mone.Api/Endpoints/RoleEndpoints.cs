using Microsoft.EntityFrameworkCore;
using Mone.Api.Authorization;
using Mone.Api.Models;
using Mone.Contracts.Models;
using Mone.Infrastructure.Data;
using Mone.Infrastructure.Data.Entities;

namespace Mone.Api.Endpoints;

public static class RoleEndpoints
{
    public static void MapRoleEndpoints(this WebApplication app)
    {
        var group = app.MapGroup("/api/roles")
            .RequireAuthorization()
            .RequirePermission(PermissionResource.Administration);

        group.MapGet("/", async (MoneDbContext db) =>
        {
            var roles = await db.MoneRoles
                .Include(r => r.Permissions)
                .Include(r => r.Assignments)
                .OrderBy(r => r.Name)
                .ToListAsync();

            return Results.Ok(roles.Select(ToResponse));
        });

        group.MapGet("/{id:guid}", async (Guid id, MoneDbContext db) =>
        {
            var role = await db.MoneRoles
                .Include(r => r.Permissions)
                .Include(r => r.Assignments)
                .FirstOrDefaultAsync(r => r.Id == id);

            return role is null ? Results.NotFound() : Results.Ok(ToResponse(role));
        });

        group.MapPost("/", async (UpsertRoleRequest request, MoneDbContext db) =>
        {
            if (string.IsNullOrWhiteSpace(request.Name))
                return Results.BadRequest("Name is required.");

            if (await db.MoneRoles.AnyAsync(r => r.Name == request.Name))
                return Results.Conflict("A role with this name already exists.");

            var now = DateTimeOffset.UtcNow;
            var role = new RoleEntity
            {
                Id = Guid.NewGuid(),
                Name = request.Name.Trim(),
                Description = request.Description,
                IsSystem = false,
                CreatedAt = now,
                UpdatedAt = now
            };

            foreach (var p in NormalizePermissions(request.Permissions))
                role.Permissions.Add(new RolePermissionEntity { RoleId = role.Id, Resource = p.Resource, Level = p.Level });

            db.MoneRoles.Add(role);
            await db.SaveChangesAsync();

            return Results.Created($"/api/roles/{role.Id}", ToResponse(role));
        });

        group.MapPut("/{id:guid}", async (Guid id, UpsertRoleRequest request, MoneDbContext db) =>
        {
            var role = await db.MoneRoles
                .Include(r => r.Permissions)
                .Include(r => r.Assignments)
                .FirstOrDefaultAsync(r => r.Id == id);

            if (role is null) return Results.NotFound();
            if (role.IsSystem) return Results.BadRequest("System roles cannot be modified.");

            if (string.IsNullOrWhiteSpace(request.Name))
                return Results.BadRequest("Name is required.");

            if (await db.MoneRoles.AnyAsync(r => r.Name == request.Name && r.Id != id))
                return Results.Conflict("A role with this name already exists.");

            role.Name = request.Name.Trim();
            role.Description = request.Description;
            role.UpdatedAt = DateTimeOffset.UtcNow;

            db.RolePermissions.RemoveRange(role.Permissions);
            role.Permissions.Clear();
            foreach (var p in NormalizePermissions(request.Permissions))
                role.Permissions.Add(new RolePermissionEntity { RoleId = role.Id, Resource = p.Resource, Level = p.Level });

            await db.SaveChangesAsync();
            return Results.Ok(ToResponse(role));
        });

        group.MapDelete("/{id:guid}", async (Guid id, MoneDbContext db) =>
        {
            var role = await db.MoneRoles
                .Include(r => r.Assignments)
                .FirstOrDefaultAsync(r => r.Id == id);

            if (role is null) return Results.NotFound();
            if (role.IsSystem) return Results.BadRequest("System roles cannot be deleted.");
            if (role.Assignments.Count > 0)
                return Results.Conflict("Cannot delete a role that is still assigned to users. Revoke its assignments first.");

            db.MoneRoles.Remove(role);
            await db.SaveChangesAsync();
            return Results.NoContent();
        });
    }

    private static IEnumerable<RolePermissionDto> NormalizePermissions(IReadOnlyList<RolePermissionDto>? permissions)
    {
        if (permissions is null) yield break;

        var seen = new HashSet<PermissionResource>();
        foreach (var p in permissions)
        {
            if (p.Level == PermissionLevel.None) continue;
            if (!seen.Add(p.Resource)) continue;
            yield return p;
        }
    }

    private static RoleResponse ToResponse(RoleEntity r) => new(
        r.Id,
        r.Name,
        r.Description,
        r.IsSystem,
        r.Permissions
            .OrderBy(p => p.Resource)
            .Select(p => new RolePermissionDto(p.Resource, p.Level))
            .ToList(),
        r.Assignments.Count,
        r.CreatedAt,
        r.UpdatedAt);
}
