using Microsoft.EntityFrameworkCore;
using Mone.Api.Models;
using Mone.Api.Services;
using Mone.Infrastructure.Data;
using Mone.Infrastructure.Data.Entities;
using NATS.Client.JetStream;

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
                .Select(p => new ProbeAssignmentResponse(p.Id, p.HostId, p.GroupId, p.ProbePluginId, p.Name, p.NameSnakeCase, p.ScheduleCron, p.ConfigJson, p.TargetAddressOverride, p.Enabled))
                .ToListAsync();

            return Results.Ok(assignments);
        });

        group.MapPost("/", async (
            Guid groupId,
            CreateProbeAssignmentRequest request,
            MoneDbContext db,
            ProbeAssignmentNameValidator nameValidator,
            ProbeAssignmentMetricMaterializer materializer,
            INatsJSContext jetStream,
            ILogger<Program> logger,
            CancellationToken ct) =>
        {
            if (!await db.HostGroups.AnyAsync(g => g.Id == groupId, ct))
                return Results.NotFound();

            var nameCheck = await nameValidator.ValidateForGroupAsync(groupId, request.Name, excludeAssignmentId: null, ct);
            if (!nameCheck.Ok)
                return Results.BadRequest(new { error = nameCheck.Error });

            var assignment = new ProbeAssignmentEntity
            {
                Id = Guid.NewGuid(),
                GroupId = groupId,
                ProbePluginId = request.ProbePluginId,
                Name = nameCheck.Name,
                NameSnakeCase = nameCheck.SnakeCase,
                ScheduleCron = request.ScheduleCron,
                ConfigJson = request.ConfigJson,
                TargetAddressOverride = request.TargetAddressOverride,
                Enabled = request.Enabled
            };

            db.ProbeAssignments.Add(assignment);
            await db.SaveChangesAsync(ct);

            await materializer.MaterializeAsync(assignment, ct);

            await ProbeScheduleNotifier.NotifyChangedAsync(jetStream, "created", assignment.Id, logger, ct);

            var response = new ProbeAssignmentResponse(assignment.Id, assignment.HostId, assignment.GroupId, assignment.ProbePluginId, assignment.Name, assignment.NameSnakeCase, assignment.ScheduleCron, assignment.ConfigJson, assignment.TargetAddressOverride, assignment.Enabled);
            return Results.Created($"/api/host-groups/{groupId}/probes/{assignment.Id}", response);
        });

        group.MapPut("/{id:guid}", async (
            Guid groupId,
            Guid id,
            UpdateProbeAssignmentRequest request,
            MoneDbContext db,
            ProbeAssignmentNameValidator nameValidator,
            ProbeAssignmentMetricMaterializer materializer,
            INatsJSContext jetStream,
            ILogger<Program> logger,
            CancellationToken ct) =>
        {
            var assignment = await db.ProbeAssignments
                .FirstOrDefaultAsync(p => p.Id == id && p.GroupId == groupId, ct);

            if (assignment is null) return Results.NotFound();

            var nameCheck = await nameValidator.ValidateForGroupAsync(groupId, request.Name, excludeAssignmentId: id, ct);
            if (!nameCheck.Ok)
                return Results.BadRequest(new { error = nameCheck.Error });

            assignment.ProbePluginId = request.ProbePluginId;
            assignment.Name = nameCheck.Name;
            assignment.NameSnakeCase = nameCheck.SnakeCase;
            assignment.ScheduleCron = request.ScheduleCron;
            assignment.ConfigJson = request.ConfigJson;
            assignment.TargetAddressOverride = request.TargetAddressOverride;
            assignment.Enabled = request.Enabled;

            await db.SaveChangesAsync(ct);

            await materializer.MaterializeAsync(assignment, ct);

            await ProbeScheduleNotifier.NotifyChangedAsync(jetStream, "updated", assignment.Id, logger, ct);

            var response = new ProbeAssignmentResponse(assignment.Id, assignment.HostId, assignment.GroupId, assignment.ProbePluginId, assignment.Name, assignment.NameSnakeCase, assignment.ScheduleCron, assignment.ConfigJson, assignment.TargetAddressOverride, assignment.Enabled);
            return Results.Ok(response);
        });

        group.MapDelete("/{id:guid}", async (
            Guid groupId,
            Guid id,
            MoneDbContext db,
            INatsJSContext jetStream,
            ILogger<Program> logger,
            CancellationToken ct) =>
        {
            var assignment = await db.ProbeAssignments
                .FirstOrDefaultAsync(p => p.Id == id && p.GroupId == groupId, ct);

            if (assignment is null) return Results.NotFound();

            db.ProbeAssignments.Remove(assignment);
            await db.SaveChangesAsync(ct);

            await ProbeScheduleNotifier.NotifyChangedAsync(jetStream, "deleted", id, logger, ct);

            return Results.NoContent();
        });
    }
}
