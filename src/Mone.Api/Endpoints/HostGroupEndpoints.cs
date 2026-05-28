using Microsoft.EntityFrameworkCore;
using Mone.Api.Models;
using Mone.Infrastructure.Data;
using Mone.Infrastructure.Data.Entities;

namespace Mone.Api.Endpoints;

public static class HostGroupEndpoints
{
    public static void MapHostGroupEndpoints(this WebApplication app)
    {
        var group = app.MapGroup("/api/host-groups").RequireAuthorization();

        group.MapGet("/", async (MoneDbContext db) =>
        {
            var groups = await db.HostGroups
                .Include(g => g.GroupMemberships)
                .Include(g => g.ChildGroups)
                .OrderBy(g => g.Name)
                .ToListAsync();

            return Results.Ok(groups.Select(ToResponse));
        });

        group.MapGet("/{id:guid}", async (Guid id, MoneDbContext db) =>
        {
            var hostGroup = await db.HostGroups
                .Include(g => g.GroupMemberships).ThenInclude(m => m.Host)
                .Include(g => g.ChildGroups)
                .FirstOrDefaultAsync(g => g.Id == id);

            if (hostGroup is null) return Results.NotFound();

            return Results.Ok(ToDetailResponse(hostGroup));
        });

        group.MapPost("/", async (CreateHostGroupRequest request, MoneDbContext db) =>
        {
            if (string.IsNullOrWhiteSpace(request.Name))
                return Results.BadRequest("Name is required.");

            if (request.ParentGroupId is { } parentId)
            {
                if (!await db.HostGroups.AnyAsync(g => g.Id == parentId))
                    return Results.BadRequest("Parent group not found.");
            }

            var now = DateTimeOffset.UtcNow;
            var hostGroup = new GroupEntity
            {
                Id = Guid.NewGuid(),
                Name = request.Name,
                Description = request.Description,
                ParentGroupId = request.ParentGroupId,
                CreatedAt = now,
                UpdatedAt = now
            };

            db.HostGroups.Add(hostGroup);
            await db.SaveChangesAsync();

            return Results.Created($"/api/host-groups/{hostGroup.Id}", ToResponse(hostGroup));
        });

        group.MapPut("/{id:guid}", async (Guid id, UpdateHostGroupRequest request, MoneDbContext db) =>
        {
            var hostGroup = await db.HostGroups
                .Include(g => g.GroupMemberships)
                .Include(g => g.ChildGroups)
                .FirstOrDefaultAsync(g => g.Id == id);

            if (hostGroup is null) return Results.NotFound();

            if (string.IsNullOrWhiteSpace(request.Name))
                return Results.BadRequest("Name is required.");

            if (request.ParentGroupId is { } parentId)
            {
                if (parentId == id)
                    return Results.BadRequest("A group cannot be its own parent.");

                if (!await db.HostGroups.AnyAsync(g => g.Id == parentId))
                    return Results.BadRequest("Parent group not found.");

                if (await WouldCreateCycle(db, id, parentId))
                    return Results.BadRequest("Setting this parent would create a circular reference.");
            }

            hostGroup.Name = request.Name;
            hostGroup.Description = request.Description;
            hostGroup.ParentGroupId = request.ParentGroupId;
            hostGroup.UpdatedAt = DateTimeOffset.UtcNow;

            await db.SaveChangesAsync();
            return Results.Ok(ToResponse(hostGroup));
        });

        group.MapDelete("/{id:guid}", async (Guid id, MoneDbContext db) =>
        {
            var hostGroup = await db.HostGroups
                .Include(g => g.ChildGroups)
                .Include(g => g.GroupMemberships)
                .FirstOrDefaultAsync(g => g.Id == id);

            if (hostGroup is null) return Results.NotFound();

            if (hostGroup.ChildGroups.Count > 0)
                return Results.BadRequest("Cannot delete a group that has child groups. Remove children first.");

            db.HostGroups.Remove(hostGroup);
            await db.SaveChangesAsync();
            return Results.NoContent();
        });

        group.MapPost("/{id:guid}/members", async (Guid id, AddGroupMemberRequest request, MoneDbContext db) =>
        {
            var hostGroup = await db.HostGroups.FindAsync(id);
            if (hostGroup is null) return Results.NotFound();

            var host = await db.Hosts.FindAsync(request.HostId);
            if (host is null) return Results.BadRequest("Host not found.");

            if (await db.HostGroupMemberships.AnyAsync(m => m.GroupId == id && m.HostId == request.HostId))
                return Results.Conflict("Host is already a member of this group.");

            db.HostGroupMemberships.Add(new GroupMembershipEntity { GroupId = id, HostId = request.HostId });
            await db.SaveChangesAsync();

            return Results.Created($"/api/host-groups/{id}/members/{request.HostId}", null);
        });

        group.MapDelete("/{id:guid}/members/{hostId:guid}", async (Guid id, Guid hostId, MoneDbContext db) =>
        {
            var membership = await db.HostGroupMemberships
                .FirstOrDefaultAsync(m => m.GroupId == id && m.HostId == hostId);

            if (membership is null) return Results.NotFound();

            db.HostGroupMemberships.Remove(membership);
            await db.SaveChangesAsync();
            return Results.NoContent();
        });
    }

    private static async Task<bool> WouldCreateCycle(MoneDbContext db, Guid groupId, Guid proposedParentId)
    {
        var currentId = proposedParentId;
        var visited = new HashSet<Guid> { groupId };

        while (currentId != default)
        {
            if (!visited.Add(currentId))
                return true;

            var parent = await db.HostGroups
                .Where(g => g.Id == currentId)
                .Select(g => g.ParentGroupId)
                .FirstOrDefaultAsync();

            currentId = parent ?? default;
        }

        return false;
    }

    private static HostGroupResponse ToResponse(GroupEntity g) => new(
        g.Id, g.Name, g.Description, g.ParentGroupId,
        g.CreatedAt, g.UpdatedAt,
        g.GroupMemberships.Count, g.ChildGroups.Count);

    private static HostGroupDetailResponse ToDetailResponse(GroupEntity g) => new(
        g.Id, g.Name, g.Description, g.ParentGroupId,
        g.CreatedAt, g.UpdatedAt,
        g.GroupMemberships.Select(m => new HostGroupMemberResponse(m.HostId, m.Host.Name)).ToArray(),
        g.ChildGroups.Select(c => new HostGroupChildResponse(c.Id, c.Name)).ToArray());
}
