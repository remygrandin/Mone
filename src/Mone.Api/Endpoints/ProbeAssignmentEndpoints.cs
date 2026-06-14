using Microsoft.EntityFrameworkCore;
using Mone.Api.Models;
using Mone.Api.Services;
using Mone.Infrastructure.Data;
using Mone.Infrastructure.Data.Entities;
using NATS.Client.JetStream;

using Mone.Api.Authorization;
using Mone.Contracts.Models;

namespace Mone.Api.Endpoints;

public static class ProbeAssignmentEndpoints
{
    public static void MapProbeAssignmentEndpoints(this WebApplication app)
    {
        var group = app.MapGroup("/api/hosts/{hostId:guid}/probes")
            .WithTags("Assignments")
            .RequireAuthorization()
            .RequirePermission(PermissionResource.Assignments);

        group.MapGet("/", async (Guid hostId, MoneDbContext db) =>
        {
            if (!await db.Hosts.AnyAsync(h => h.Id == hostId))
                return Results.Problem("Host not found.", statusCode: StatusCodes.Status404NotFound);

            var assignments = await db.ProbeAssignments
                .Where(p => p.HostId == hostId)
                .Select(p => new ProbeAssignmentResponse(p.Id, p.HostId, p.GroupId, p.ProbePluginId, p.Name, p.NameSnakeCase, p.ScheduleCron, p.ConfigJson, p.TargetAddressOverride, p.Enabled, p.ExecutorNodeId))
                .ToListAsync();

            return Results.Ok(assignments);
        })
        .WithName("ListProbeAssignments")
        .WithSummary("List probe assignments for a host. Returns 404 if the host does not exist.")
        .Produces<IEnumerable<ProbeAssignmentResponse>>()
        .ProducesProblem(StatusCodes.Status404NotFound);

        group.MapPost("/", async (
            Guid hostId,
            CreateProbeAssignmentRequest request,
            MoneDbContext db,
            ProbeAssignmentNameValidator nameValidator,
            ProbeAssignmentMetricMaterializer materializer,
            INatsJSContext jetStream,
            ILogger<Program> logger,
            CancellationToken ct) =>
        {
            if (!await db.Hosts.AnyAsync(h => h.Id == hostId, ct))
                return Results.Problem("Host not found.", statusCode: StatusCodes.Status404NotFound);

            var nameCheck = await nameValidator.ValidateForHostAsync(hostId, request.Name, excludeAssignmentId: null, ct);
            if (!nameCheck.Ok)
                return Results.ValidationProblem(new Dictionary<string, string[]> { ["name"] = new[] { nameCheck.Error ?? "Invalid name." } });

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
                Enabled = request.Enabled,
                ExecutorNodeId = request.ExecutorNodeId
            };

            db.ProbeAssignments.Add(assignment);
            await db.SaveChangesAsync(ct);

            await materializer.MaterializeAsync(assignment, ct);

            await ProbeScheduleNotifier.NotifyChangedAsync(jetStream, "created", assignment.Id, logger, ct);

            var response = new ProbeAssignmentResponse(assignment.Id, assignment.HostId, assignment.GroupId, assignment.ProbePluginId, assignment.Name, assignment.NameSnakeCase, assignment.ScheduleCron, assignment.ConfigJson, assignment.TargetAddressOverride, assignment.Enabled, assignment.ExecutorNodeId);
            return Results.Created($"/api/hosts/{hostId}/probes/{assignment.Id}", response);
        })
        .WithName("CreateProbeAssignment")
        .WithSummary("Create a probe assignment on a host. Returns 404 if the host is missing, 400 if the name is invalid.")
        .Produces<ProbeAssignmentResponse>(StatusCodes.Status201Created)
        .ProducesProblem(StatusCodes.Status404NotFound)
        .ProducesValidationProblem();

        group.MapPut("/{id:guid}", async (
            Guid hostId,
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
                .FirstOrDefaultAsync(p => p.Id == id && p.HostId == hostId, ct);

            if (assignment is null) return Results.Problem("Probe assignment not found.", statusCode: StatusCodes.Status404NotFound);

            var nameCheck = await nameValidator.ValidateForHostAsync(hostId, request.Name, excludeAssignmentId: id, ct);
            if (!nameCheck.Ok)
                return Results.ValidationProblem(new Dictionary<string, string[]> { ["name"] = new[] { nameCheck.Error ?? "Invalid name." } });

            assignment.ProbePluginId = request.ProbePluginId;
            assignment.Name = nameCheck.Name;
            assignment.NameSnakeCase = nameCheck.SnakeCase;
            assignment.ScheduleCron = request.ScheduleCron;
            assignment.ConfigJson = request.ConfigJson;
            assignment.TargetAddressOverride = request.TargetAddressOverride;
            assignment.Enabled = request.Enabled;
            assignment.ExecutorNodeId = request.ExecutorNodeId;

            await db.SaveChangesAsync(ct);

            await materializer.MaterializeAsync(assignment, ct);

            await ProbeScheduleNotifier.NotifyChangedAsync(jetStream, "updated", assignment.Id, logger, ct);

            var response = new ProbeAssignmentResponse(assignment.Id, assignment.HostId, assignment.GroupId, assignment.ProbePluginId, assignment.Name, assignment.NameSnakeCase, assignment.ScheduleCron, assignment.ConfigJson, assignment.TargetAddressOverride, assignment.Enabled, assignment.ExecutorNodeId);
            return Results.Ok(response);
        })
        .WithName("UpdateProbeAssignment")
        .WithSummary("Update a probe assignment on a host. Returns 404 if not found, 400 if the name is invalid.")
        .Produces<ProbeAssignmentResponse>()
        .ProducesProblem(StatusCodes.Status404NotFound)
        .ProducesValidationProblem();

        group.MapDelete("/{id:guid}", async (
            Guid hostId,
            Guid id,
            MoneDbContext db,
            INatsJSContext jetStream,
            ILogger<Program> logger,
            CancellationToken ct) =>
        {
            var assignment = await db.ProbeAssignments
                .FirstOrDefaultAsync(p => p.Id == id && p.HostId == hostId, ct);

            if (assignment is null) return Results.Problem("Probe assignment not found.", statusCode: StatusCodes.Status404NotFound);

            db.ProbeAssignments.Remove(assignment);
            await db.SaveChangesAsync(ct);

            await ProbeScheduleNotifier.NotifyChangedAsync(jetStream, "deleted", id, logger, ct);

            return Results.NoContent();
        })
        .WithName("DeleteProbeAssignment")
        .WithSummary("Delete a probe assignment from a host. Returns 404 if the assignment does not exist.")
        .Produces(StatusCodes.Status204NoContent)
        .ProducesProblem(StatusCodes.Status404NotFound);
    }
}
