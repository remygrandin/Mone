using Microsoft.EntityFrameworkCore;
using Mone.Api.Models;
using Mone.Api.Services;
using Mone.Infrastructure.Data;
using Mone.Infrastructure.Data.Entities;

namespace Mone.Api.Endpoints;

public static class ProbeAssignmentEndpoints
{
    public static void MapProbeAssignmentEndpoints(this WebApplication app)
    {
        var group = app.MapGroup("/api/hosts/{hostId:guid}/probes").RequireAuthorization();

        group.MapGet("/", async (Guid hostId, MoneDbContext db) =>
        {
            if (!await db.Hosts.AnyAsync(h => h.Id == hostId))
                return Results.NotFound();

            var assignments = await db.ProbeAssignments
                .Where(p => p.HostId == hostId)
                .Select(p => new ProbeAssignmentResponse(p.Id, p.HostId, p.GroupId, p.ProbePluginId, p.Name, p.NameSnakeCase, p.ScheduleCron, p.ConfigJson, p.TargetAddressOverride, p.Enabled))
                .ToListAsync();

            return Results.Ok(assignments);
        });

        group.MapPost("/", async (
            Guid hostId,
            CreateProbeAssignmentRequest request,
            MoneDbContext db,
            ProbeAssignmentNameValidator nameValidator,
            ProbeAssignmentMetricMaterializer materializer,
            CancellationToken ct) =>
        {
            if (!await db.Hosts.AnyAsync(h => h.Id == hostId, ct))
                return Results.NotFound();

            var nameCheck = await nameValidator.ValidateForHostAsync(hostId, request.Name, excludeAssignmentId: null, ct);
            if (!nameCheck.Ok)
                return Results.BadRequest(new { error = nameCheck.Error });

            var assignment = new ProbeAssignmentEntity
            {
                Id = Guid.NewGuid(),
                HostId = hostId,
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

            var response = new ProbeAssignmentResponse(assignment.Id, assignment.HostId, assignment.GroupId, assignment.ProbePluginId, assignment.Name, assignment.NameSnakeCase, assignment.ScheduleCron, assignment.ConfigJson, assignment.TargetAddressOverride, assignment.Enabled);
            return Results.Created($"/api/hosts/{hostId}/probes/{assignment.Id}", response);
        });

        group.MapPut("/{id:guid}", async (
            Guid hostId,
            Guid id,
            UpdateProbeAssignmentRequest request,
            MoneDbContext db,
            ProbeAssignmentNameValidator nameValidator,
            ProbeAssignmentMetricMaterializer materializer,
            CancellationToken ct) =>
        {
            var assignment = await db.ProbeAssignments
                .FirstOrDefaultAsync(p => p.Id == id && p.HostId == hostId, ct);

            if (assignment is null) return Results.NotFound();

            var nameCheck = await nameValidator.ValidateForHostAsync(hostId, request.Name, excludeAssignmentId: id, ct);
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

            var response = new ProbeAssignmentResponse(assignment.Id, assignment.HostId, assignment.GroupId, assignment.ProbePluginId, assignment.Name, assignment.NameSnakeCase, assignment.ScheduleCron, assignment.ConfigJson, assignment.TargetAddressOverride, assignment.Enabled);
            return Results.Ok(response);
        });

        group.MapDelete("/{id:guid}", async (Guid hostId, Guid id, MoneDbContext db) =>
        {
            var assignment = await db.ProbeAssignments
                .FirstOrDefaultAsync(p => p.Id == id && p.HostId == hostId);

            if (assignment is null) return Results.NotFound();

            db.ProbeAssignments.Remove(assignment);
            await db.SaveChangesAsync();
            return Results.NoContent();
        });
    }
}
