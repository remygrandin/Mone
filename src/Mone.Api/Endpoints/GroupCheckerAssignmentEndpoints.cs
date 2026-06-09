using Microsoft.EntityFrameworkCore;
using Mone.Api.Models;
using Mone.Api.Services;
using Mone.Infrastructure.Data;
using Mone.Infrastructure.Data.Entities;

namespace Mone.Api.Endpoints;

public static class GroupCheckerAssignmentEndpoints
{
    public static void MapGroupCheckerAssignmentEndpoints(this WebApplication app)
    {
        var group = app.MapGroup("/api/host-groups/{groupId:guid}/checkers").RequireAuthorization();

        group.MapGet("/", async (Guid groupId, MoneDbContext db) =>
        {
            if (!await db.HostGroups.AnyAsync(g => g.Id == groupId))
                return Results.NotFound();

            var assignments = await db.CheckerAssignments
                .Where(c => c.GroupId == groupId)
                .Select(c => new CheckerAssignmentResponse(c.Id, c.HostId, c.GroupId, c.CheckerPluginId, c.Name, c.NameSnakeCase, c.ConfigJson, c.Enabled))
                .ToListAsync();

            return Results.Ok(assignments);
        });

        group.MapPost("/", async (Guid groupId, CreateCheckerAssignmentRequest request, MoneDbContext db, CheckerAssignmentNameValidator nameValidator, CancellationToken ct) =>
        {
            if (!await db.HostGroups.AnyAsync(g => g.Id == groupId, ct))
                return Results.NotFound();

            var nameCheck = await nameValidator.ValidateForGroupAsync(groupId, request.Name, excludeAssignmentId: null, ct);
            if (!nameCheck.Ok)
                return Results.BadRequest(new { error = nameCheck.Error });

            var assignment = new CheckerAssignmentEntity
            {
                Id = Guid.NewGuid(),
                GroupId = groupId,
                CheckerPluginId = request.CheckerPluginId,
                Name = nameCheck.Name,
                NameSnakeCase = nameCheck.SnakeCase,
                ConfigJson = request.ConfigJson,
                Enabled = request.Enabled
            };

            db.CheckerAssignments.Add(assignment);
            await db.SaveChangesAsync(ct);

            var response = new CheckerAssignmentResponse(assignment.Id, assignment.HostId, assignment.GroupId, assignment.CheckerPluginId, assignment.Name, assignment.NameSnakeCase, assignment.ConfigJson, assignment.Enabled);
            return Results.Created($"/api/host-groups/{groupId}/checkers/{assignment.Id}", response);
        });

        group.MapPut("/{id:guid}", async (Guid groupId, Guid id, UpdateCheckerAssignmentRequest request, MoneDbContext db, CheckerAssignmentNameValidator nameValidator, CancellationToken ct) =>
        {
            var assignment = await db.CheckerAssignments
                .FirstOrDefaultAsync(c => c.Id == id && c.GroupId == groupId, ct);

            if (assignment is null) return Results.NotFound();

            var nameCheck = await nameValidator.ValidateForGroupAsync(groupId, request.Name, excludeAssignmentId: id, ct);
            if (!nameCheck.Ok)
                return Results.BadRequest(new { error = nameCheck.Error });

            assignment.CheckerPluginId = request.CheckerPluginId;
            assignment.Name = nameCheck.Name;
            assignment.NameSnakeCase = nameCheck.SnakeCase;
            assignment.ConfigJson = request.ConfigJson;
            assignment.Enabled = request.Enabled;

            await db.SaveChangesAsync(ct);

            var response = new CheckerAssignmentResponse(assignment.Id, assignment.HostId, assignment.GroupId, assignment.CheckerPluginId, assignment.Name, assignment.NameSnakeCase, assignment.ConfigJson, assignment.Enabled);
            return Results.Ok(response);
        });

        group.MapDelete("/{id:guid}", async (Guid groupId, Guid id, MoneDbContext db) =>
        {
            var assignment = await db.CheckerAssignments
                .FirstOrDefaultAsync(c => c.Id == id && c.GroupId == groupId);

            if (assignment is null) return Results.NotFound();

            db.CheckerAssignments.Remove(assignment);
            await db.SaveChangesAsync();
            return Results.NoContent();
        });
    }
}
