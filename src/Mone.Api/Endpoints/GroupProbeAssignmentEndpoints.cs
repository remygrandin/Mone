using Microsoft.EntityFrameworkCore;
using Mone.Api.Models;
using Mone.Infrastructure.Data;
using Mone.Infrastructure.Data.Entities;

namespace Mone.Api.Endpoints;

public static class GroupProbeAssignmentEndpoints
{
    public static void MapGroupProbeAssignmentEndpoints(this WebApplication app)
    {
        var group = app.MapGroup("/api/host-groups/{groupId:guid}/probes").RequireAuthorization();

        group.MapGet("/", async (Guid groupId, MoneDbContext db) =>
        {
            if (!await db.HostGroups.AnyAsync(g => g.Id == groupId))
                return Results.NotFound();

            var assignments = await db.ProbeAssignments
                .Where(p => p.GroupId == groupId)
                .Select(p => new ProbeAssignmentResponse(p.Id, p.HostId, p.GroupId, p.ProbePluginId, p.ScheduleCron, p.ConfigJson, p.TargetAddressOverride, p.Enabled))
                .ToListAsync();

            return Results.Ok(assignments);
        });

        group.MapPost("/", async (Guid groupId, CreateProbeAssignmentRequest request, MoneDbContext db) =>
        {
            if (!await db.HostGroups.AnyAsync(g => g.Id == groupId))
                return Results.NotFound();

            var assignment = new ProbeAssignmentEntity
            {
                Id = Guid.NewGuid(),
                GroupId = groupId,
                ProbePluginId = request.ProbePluginId,
                ScheduleCron = request.ScheduleCron,
                ConfigJson = request.ConfigJson,
                TargetAddressOverride = request.TargetAddressOverride,
                Enabled = request.Enabled
            };

            db.ProbeAssignments.Add(assignment);
            await db.SaveChangesAsync();

            var response = new ProbeAssignmentResponse(assignment.Id, assignment.HostId, assignment.GroupId, assignment.ProbePluginId, assignment.ScheduleCron, assignment.ConfigJson, assignment.TargetAddressOverride, assignment.Enabled);
            return Results.Created($"/api/host-groups/{groupId}/probes/{assignment.Id}", response);
        });

        group.MapPut("/{id:guid}", async (Guid groupId, Guid id, UpdateProbeAssignmentRequest request, MoneDbContext db) =>
        {
            var assignment = await db.ProbeAssignments
                .FirstOrDefaultAsync(p => p.Id == id && p.GroupId == groupId);

            if (assignment is null) return Results.NotFound();

            assignment.ProbePluginId = request.ProbePluginId;
            assignment.ScheduleCron = request.ScheduleCron;
            assignment.ConfigJson = request.ConfigJson;
            assignment.TargetAddressOverride = request.TargetAddressOverride;
            assignment.Enabled = request.Enabled;

            await db.SaveChangesAsync();

            var response = new ProbeAssignmentResponse(assignment.Id, assignment.HostId, assignment.GroupId, assignment.ProbePluginId, assignment.ScheduleCron, assignment.ConfigJson, assignment.TargetAddressOverride, assignment.Enabled);
            return Results.Ok(response);
        });

        group.MapDelete("/{id:guid}", async (Guid groupId, Guid id, MoneDbContext db) =>
        {
            var assignment = await db.ProbeAssignments
                .FirstOrDefaultAsync(p => p.Id == id && p.GroupId == groupId);

            if (assignment is null) return Results.NotFound();

            db.ProbeAssignments.Remove(assignment);
            await db.SaveChangesAsync();
            return Results.NoContent();
        });
    }
}
