using Microsoft.EntityFrameworkCore;
using Mone.Api.Models;
using Mone.Api.Services;
using Mone.Infrastructure.Data;
using Mone.Infrastructure.Data.Entities;

using Mone.Api.Authorization;
using Mone.Contracts.Models;

namespace Mone.Api.Endpoints;

public static class HostGroupEndpoints
{
    public static void MapHostGroupEndpoints(this WebApplication app)
    {
        var group = app.MapGroup("/api/host-groups")
            .WithTags("Host Groups")
            .RequireAuthorization()
            .RequirePermission(PermissionResource.Groups);

        group.MapGet("/", async (MoneDbContext db) =>
        {
            var groups = await db.HostGroups
                .Include(g => g.GroupMemberships)
                .Include(g => g.ChildGroups)
                .OrderBy(g => g.Name)
                .ToListAsync();

            return Results.Ok(groups.Select(ToResponse));
        })
        .WithName("ListHostGroups")
        .WithSummary("List all host groups ordered by name, with membership and child-group counts.")
        .Produces<IEnumerable<HostGroupResponse>>();

        group.MapGet("/{id:guid}", async (Guid id, MoneDbContext db) =>
        {
            var hostGroup = await db.HostGroups
                .Include(g => g.GroupMemberships).ThenInclude(m => m.Host)
                .Include(g => g.ChildGroups)
                .FirstOrDefaultAsync(g => g.Id == id);

            if (hostGroup is null) return Results.Problem("Host group not found.", statusCode: StatusCodes.Status404NotFound);

            return Results.Ok(ToDetailResponse(hostGroup));
        })
        .WithName("GetHostGroup")
        .WithSummary("Get a single host group by id, including its members and child groups.")
        .Produces<HostGroupDetailResponse>()
        .ProducesProblem(StatusCodes.Status404NotFound);

        group.MapPost("/", async (CreateHostGroupRequest request, MoneDbContext db) =>
        {
            if (string.IsNullOrWhiteSpace(request.Name))
                return Results.ValidationProblem(new Dictionary<string, string[]> { ["Name"] = new[] { "Name is required." } });

            if (request.ParentGroupId is { } parentId)
            {
                if (!await db.HostGroups.AnyAsync(g => g.Id == parentId))
                    return Results.ValidationProblem(new Dictionary<string, string[]> { ["ParentGroupId"] = new[] { "Parent group not found." } });
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
        })
        .WithName("CreateHostGroup")
        .WithSummary("Create a new host group. Validates the name and optional parent group.")
        .Produces<HostGroupResponse>(StatusCodes.Status201Created)
        .ProducesValidationProblem();

        group.MapPut("/{id:guid}", async (Guid id, UpdateHostGroupRequest request, MoneDbContext db) =>
        {
            var hostGroup = await db.HostGroups
                .Include(g => g.GroupMemberships)
                .Include(g => g.ChildGroups)
                .FirstOrDefaultAsync(g => g.Id == id);

            if (hostGroup is null) return Results.Problem("Host group not found.", statusCode: StatusCodes.Status404NotFound);

            if (string.IsNullOrWhiteSpace(request.Name))
                return Results.ValidationProblem(new Dictionary<string, string[]> { ["Name"] = new[] { "Name is required." } });

            if (request.ParentGroupId is { } parentId)
            {
                if (parentId == id)
                    return Results.ValidationProblem(new Dictionary<string, string[]> { ["ParentGroupId"] = new[] { "A group cannot be its own parent." } });

                if (!await db.HostGroups.AnyAsync(g => g.Id == parentId))
                    return Results.ValidationProblem(new Dictionary<string, string[]> { ["ParentGroupId"] = new[] { "Parent group not found." } });

                if (await WouldCreateCycle(db, id, parentId))
                    return Results.ValidationProblem(new Dictionary<string, string[]> { ["ParentGroupId"] = new[] { "Setting this parent would create a circular reference." } });
            }

            hostGroup.Name = request.Name;
            hostGroup.Description = request.Description;
            hostGroup.ParentGroupId = request.ParentGroupId;
            hostGroup.UpdatedAt = DateTimeOffset.UtcNow;

            await db.SaveChangesAsync();
            return Results.Ok(ToResponse(hostGroup));
        })
        .WithName("UpdateHostGroup")
        .WithSummary("Update a host group. Returns 404 if not found; validates name and parent (including cycle detection).")
        .Produces<HostGroupResponse>()
        .ProducesValidationProblem()
        .ProducesProblem(StatusCodes.Status404NotFound);

        group.MapDelete("/{id:guid}", async (Guid id, MoneDbContext db) =>
        {
            var hostGroup = await db.HostGroups
                .Include(g => g.ChildGroups)
                .Include(g => g.GroupMemberships)
                .FirstOrDefaultAsync(g => g.Id == id);

            if (hostGroup is null) return Results.Problem("Host group not found.", statusCode: StatusCodes.Status404NotFound);

            if (hostGroup.ChildGroups.Count > 0)
                return Results.Problem("Cannot delete a group that has child groups. Remove children first.", statusCode: StatusCodes.Status400BadRequest);

            db.HostGroups.Remove(hostGroup);
            await db.SaveChangesAsync();
            return Results.NoContent();
        })
        .WithName("DeleteHostGroup")
        .WithSummary("Delete a host group. Returns 404 if not found, or 400 if the group still has child groups.")
        .Produces(StatusCodes.Status204NoContent)
        .ProducesProblem(StatusCodes.Status400BadRequest)
        .ProducesProblem(StatusCodes.Status404NotFound);

        group.MapPost("/{id:guid}/members", async (
            Guid id,
            AddGroupMemberRequest request,
            MoneDbContext db,
            ProbeAssignmentNameValidator nameValidator,
            CheckerAssignmentNameValidator checkerNameValidator,
            CancellationToken ct) =>
        {
            var hostGroup = await db.HostGroups.FindAsync(new object[] { id }, ct);
            if (hostGroup is null) return Results.Problem("Host group not found.", statusCode: StatusCodes.Status404NotFound);

            var host = await db.Hosts.FindAsync(new object[] { request.HostId }, ct);
            if (host is null) return Results.ValidationProblem(new Dictionary<string, string[]> { ["HostId"] = new[] { "Host not found." } });

            if (await db.HostGroupMemberships.AnyAsync(m => m.GroupId == id && m.HostId == request.HostId, ct))
                return Results.Problem("Host is already a member of this group.", statusCode: StatusCodes.Status409Conflict);

            var collision = await nameValidator.ValidateMembershipAddAsync(id, request.HostId, ct);
            if (collision is not null)
                return Results.ValidationProblem(new Dictionary<string, string[]> { ["HostId"] = new[] { collision } });

            var checkerCollision = await checkerNameValidator.ValidateMembershipAddAsync(id, request.HostId, ct);
            if (checkerCollision is not null)
                return Results.ValidationProblem(new Dictionary<string, string[]> { ["HostId"] = new[] { checkerCollision } });

            db.HostGroupMemberships.Add(new GroupMembershipEntity { GroupId = id, HostId = request.HostId });
            await db.SaveChangesAsync(ct);

            return Results.Created($"/api/host-groups/{id}/members/{request.HostId}", null);
        })
        .WithName("AddHostGroupMember")
        .WithSummary("Add a host to a group. Returns 404 if the group is missing, 409 if already a member, or a validation problem on an unknown host or name collision.")
        .Produces(StatusCodes.Status201Created)
        .ProducesValidationProblem()
        .ProducesProblem(StatusCodes.Status404NotFound)
        .ProducesProblem(StatusCodes.Status409Conflict);

        group.MapDelete("/{id:guid}/members/{hostId:guid}", async (Guid id, Guid hostId, MoneDbContext db) =>
        {
            var membership = await db.HostGroupMemberships
                .FirstOrDefaultAsync(m => m.GroupId == id && m.HostId == hostId);

            if (membership is null) return Results.Problem("Group membership not found.", statusCode: StatusCodes.Status404NotFound);

            db.HostGroupMemberships.Remove(membership);
            await db.SaveChangesAsync();
            return Results.NoContent();
        })
        .WithName("RemoveHostGroupMember")
        .WithSummary("Remove a host from a group. Returns 404 if the membership does not exist.")
        .Produces(StatusCodes.Status204NoContent)
        .ProducesProblem(StatusCodes.Status404NotFound);
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
