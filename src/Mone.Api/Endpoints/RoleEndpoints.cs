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
            .WithTags("Roles")
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
        })
        .WithName("ListRoles")
        .WithSummary("List all roles with their permissions and assignment counts, ordered by name.")
        .Produces<IEnumerable<RoleResponse>>();

        group.MapGet("/{id:guid}", async (Guid id, MoneDbContext db) =>
        {
            var role = await db.MoneRoles
                .Include(r => r.Permissions)
                .Include(r => r.Assignments)
                .FirstOrDefaultAsync(r => r.Id == id);

            return role is null
                ? Results.Problem("Role not found.", statusCode: StatusCodes.Status404NotFound)
                : Results.Ok(ToResponse(role));
        })
        .WithName("GetRole")
        .WithSummary("Get a single role by id. Returns 404 if the role does not exist.")
        .Produces<RoleResponse>()
        .ProducesProblem(StatusCodes.Status404NotFound);

        group.MapPost("/", async (UpsertRoleRequest request, MoneDbContext db) =>
        {
            if (string.IsNullOrWhiteSpace(request.Name))
                return Results.ValidationProblem(new Dictionary<string, string[]> { ["Name"] = new[] { "Name is required." } });

            if (await db.MoneRoles.AnyAsync(r => r.Name == request.Name))
                return Results.Problem("A role with this name already exists.", statusCode: StatusCodes.Status409Conflict);

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
        })
        .WithName("CreateRole")
        .WithSummary("Create a new role. Returns 409 if a role with the same name already exists.")
        .Produces<RoleResponse>(StatusCodes.Status201Created)
        .ProducesValidationProblem()
        .ProducesProblem(StatusCodes.Status409Conflict);

        group.MapPut("/{id:guid}", async (Guid id, UpsertRoleRequest request, MoneDbContext db) =>
        {
            var role = await db.MoneRoles
                .Include(r => r.Permissions)
                .Include(r => r.Assignments)
                .FirstOrDefaultAsync(r => r.Id == id);

            if (role is null) return Results.Problem("Role not found.", statusCode: StatusCodes.Status404NotFound);
            if (role.IsSystem) return Results.Problem("System roles cannot be modified.", statusCode: StatusCodes.Status400BadRequest);

            if (string.IsNullOrWhiteSpace(request.Name))
                return Results.ValidationProblem(new Dictionary<string, string[]> { ["Name"] = new[] { "Name is required." } });

            if (await db.MoneRoles.AnyAsync(r => r.Name == request.Name && r.Id != id))
                return Results.Problem("A role with this name already exists.", statusCode: StatusCodes.Status409Conflict);

            role.Name = request.Name.Trim();
            role.Description = request.Description;
            role.UpdatedAt = DateTimeOffset.UtcNow;

            db.RolePermissions.RemoveRange(role.Permissions);
            role.Permissions.Clear();
            foreach (var p in NormalizePermissions(request.Permissions))
                role.Permissions.Add(new RolePermissionEntity { RoleId = role.Id, Resource = p.Resource, Level = p.Level });

            await db.SaveChangesAsync();
            return Results.Ok(ToResponse(role));
        })
        .WithName("UpdateRole")
        .WithSummary("Update an existing role. Returns 400 for system roles, 404 if not found, 409 on a name collision.")
        .Produces<RoleResponse>()
        .ProducesValidationProblem()
        .ProducesProblem(StatusCodes.Status400BadRequest)
        .ProducesProblem(StatusCodes.Status404NotFound)
        .ProducesProblem(StatusCodes.Status409Conflict);

        group.MapDelete("/{id:guid}", async (Guid id, MoneDbContext db) =>
        {
            var role = await db.MoneRoles
                .Include(r => r.Assignments)
                .FirstOrDefaultAsync(r => r.Id == id);

            if (role is null) return Results.Problem("Role not found.", statusCode: StatusCodes.Status404NotFound);
            if (role.IsSystem) return Results.Problem("System roles cannot be deleted.", statusCode: StatusCodes.Status400BadRequest);
            if (role.Assignments.Count > 0)
                return Results.Problem("Cannot delete a role that is still assigned to users. Revoke its assignments first.", statusCode: StatusCodes.Status409Conflict);

            db.MoneRoles.Remove(role);
            await db.SaveChangesAsync();
            return Results.NoContent();
        })
        .WithName("DeleteRole")
        .WithSummary("Delete a role by id. Returns 400 for system roles, 404 if not found, 409 if still assigned to users.")
        .Produces(StatusCodes.Status204NoContent)
        .ProducesProblem(StatusCodes.Status400BadRequest)
        .ProducesProblem(StatusCodes.Status404NotFound)
        .ProducesProblem(StatusCodes.Status409Conflict);
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
