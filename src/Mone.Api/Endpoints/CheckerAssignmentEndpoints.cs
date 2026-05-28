using Microsoft.EntityFrameworkCore;
using Mone.Api.Models;
using Mone.Infrastructure.Data;
using Mone.Infrastructure.Data.Entities;

namespace Mone.Api.Endpoints;

public static class CheckerAssignmentEndpoints
{
    public static void MapCheckerAssignmentEndpoints(this WebApplication app)
    {
        var group = app.MapGroup("/api/hosts/{hostId:guid}/checkers").RequireAuthorization();

        group.MapGet("/", async (Guid hostId, MoneDbContext db) =>
        {
            if (!await db.Hosts.AnyAsync(h => h.Id == hostId))
                return Results.NotFound();

            var assignments = await db.CheckerAssignments
                .Where(c => c.HostId == hostId)
                .Select(c => new CheckerAssignmentResponse(c.Id, c.HostId, c.GroupId, c.CheckerPluginId, c.ConfigJson, c.Enabled))
                .ToListAsync();

            return Results.Ok(assignments);
        });

        group.MapPost("/", async (Guid hostId, CreateCheckerAssignmentRequest request, MoneDbContext db) =>
        {
            if (!await db.Hosts.AnyAsync(h => h.Id == hostId))
                return Results.NotFound();

            var assignment = new CheckerAssignmentEntity
            {
                Id = Guid.NewGuid(),
                HostId = hostId,
                CheckerPluginId = request.CheckerPluginId,
                ConfigJson = request.ConfigJson,
                Enabled = request.Enabled
            };

            db.CheckerAssignments.Add(assignment);
            await db.SaveChangesAsync();

            var response = new CheckerAssignmentResponse(assignment.Id, assignment.HostId, assignment.GroupId, assignment.CheckerPluginId, assignment.ConfigJson, assignment.Enabled);
            return Results.Created($"/api/hosts/{hostId}/checkers/{assignment.Id}", response);
        });

        group.MapPut("/{id:guid}", async (Guid hostId, Guid id, UpdateCheckerAssignmentRequest request, MoneDbContext db) =>
        {
            var assignment = await db.CheckerAssignments
                .FirstOrDefaultAsync(c => c.Id == id && c.HostId == hostId);

            if (assignment is null) return Results.NotFound();

            assignment.CheckerPluginId = request.CheckerPluginId;
            assignment.ConfigJson = request.ConfigJson;
            assignment.Enabled = request.Enabled;

            await db.SaveChangesAsync();

            var response = new CheckerAssignmentResponse(assignment.Id, assignment.HostId, assignment.GroupId, assignment.CheckerPluginId, assignment.ConfigJson, assignment.Enabled);
            return Results.Ok(response);
        });

        group.MapDelete("/{id:guid}", async (Guid hostId, Guid id, MoneDbContext db) =>
        {
            var assignment = await db.CheckerAssignments
                .FirstOrDefaultAsync(c => c.Id == id && c.HostId == hostId);

            if (assignment is null) return Results.NotFound();

            db.CheckerAssignments.Remove(assignment);
            await db.SaveChangesAsync();
            return Results.NoContent();
        });
    }
}
