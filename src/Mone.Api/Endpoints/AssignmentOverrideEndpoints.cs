using Microsoft.EntityFrameworkCore;
using Mone.Api.Models;
using Mone.Infrastructure.Data;
using Mone.Infrastructure.Data.Entities;

using Mone.Api.Authorization;
using Mone.Contracts.Models;

namespace Mone.Api.Endpoints;

public static class AssignmentOverrideEndpoints
{
    public static void MapAssignmentOverrideEndpoints(this WebApplication app)
    {
        app.MapPut("/api/hosts/{hostId:guid}/overrides/probes/{assignmentId:guid}", async (
            Guid hostId, Guid assignmentId, UpsertOverrideRequest request, MoneDbContext db) =>
        {
            if (!await db.Hosts.AnyAsync(h => h.Id == hostId))
                return Results.Problem("Host not found.", statusCode: StatusCodes.Status404NotFound);

            var assignment = await db.ProbeAssignments.FindAsync(assignmentId);
            if (assignment is null)
                return Results.Problem("Probe assignment not found.", statusCode: StatusCodes.Status404NotFound);
            if (assignment.GroupId is null)
                return Results.Problem("Cannot override a direct host assignment. Overrides apply only to group-level assignments.", statusCode: StatusCodes.Status400BadRequest);

            var existing = await db.ProbeAssignmentOverrides
                .FirstOrDefaultAsync(o => o.HostId == hostId && o.ProbeAssignmentId == assignmentId);

            var now = DateTime.UtcNow;
            if (existing is not null)
            {
                existing.ConfigJsonOverride = request.ConfigJsonOverride;
                existing.IsDisabled = request.IsDisabled;
                existing.UpdatedAt = now;
            }
            else
            {
                existing = new ProbeAssignmentOverrideEntity
                {
                    Id = Guid.NewGuid(),
                    HostId = hostId,
                    ProbeAssignmentId = assignmentId,
                    ConfigJsonOverride = request.ConfigJsonOverride,
                    IsDisabled = request.IsDisabled,
                    CreatedAt = now,
                    UpdatedAt = now
                };
                db.ProbeAssignmentOverrides.Add(existing);
            }

            await db.SaveChangesAsync();
            return Results.Ok(ToProbeResponse(existing));
        })
        .WithTags("Assignments")
        .WithName("UpsertProbeOverride")
        .WithSummary("Create or update a group-level probe assignment override for a host. Returns 404 if host/assignment missing, 400 for a direct (non-group) assignment.")
        .Produces<OverrideResponse>()
        .ProducesProblem(StatusCodes.Status404NotFound)
        .ProducesProblem(StatusCodes.Status400BadRequest)
        .RequireAuthorization().RequirePermission(PermissionResource.Assignments);

        app.MapDelete("/api/hosts/{hostId:guid}/overrides/probes/{assignmentId:guid}", async (
            Guid hostId, Guid assignmentId, MoneDbContext db) =>
        {
            var existing = await db.ProbeAssignmentOverrides
                .FirstOrDefaultAsync(o => o.HostId == hostId && o.ProbeAssignmentId == assignmentId);
            if (existing is null)
                return Results.Problem("Probe assignment override not found.", statusCode: StatusCodes.Status404NotFound);

            db.ProbeAssignmentOverrides.Remove(existing);
            await db.SaveChangesAsync();
            return Results.NoContent();
        })
        .WithTags("Assignments")
        .WithName("DeleteProbeOverride")
        .WithSummary("Remove a probe assignment override for a host. Returns 404 if the override does not exist.")
        .Produces(StatusCodes.Status204NoContent)
        .ProducesProblem(StatusCodes.Status404NotFound)
        .RequireAuthorization().RequirePermission(PermissionResource.Assignments);

        app.MapPut("/api/hosts/{hostId:guid}/overrides/checkers/{assignmentId:guid}", async (
            Guid hostId, Guid assignmentId, UpsertOverrideRequest request, MoneDbContext db) =>
        {
            if (!await db.Hosts.AnyAsync(h => h.Id == hostId))
                return Results.Problem("Host not found.", statusCode: StatusCodes.Status404NotFound);

            var assignment = await db.CheckerAssignments.FindAsync(assignmentId);
            if (assignment is null)
                return Results.Problem("Checker assignment not found.", statusCode: StatusCodes.Status404NotFound);
            if (assignment.GroupId is null)
                return Results.Problem("Cannot override a direct host assignment. Overrides apply only to group-level assignments.", statusCode: StatusCodes.Status400BadRequest);

            var existing = await db.CheckerAssignmentOverrides
                .FirstOrDefaultAsync(o => o.HostId == hostId && o.CheckerAssignmentId == assignmentId);

            var now = DateTime.UtcNow;
            if (existing is not null)
            {
                existing.ConfigJsonOverride = request.ConfigJsonOverride;
                existing.IsDisabled = request.IsDisabled;
                existing.UpdatedAt = now;
            }
            else
            {
                existing = new CheckerAssignmentOverrideEntity
                {
                    Id = Guid.NewGuid(),
                    HostId = hostId,
                    CheckerAssignmentId = assignmentId,
                    ConfigJsonOverride = request.ConfigJsonOverride,
                    IsDisabled = request.IsDisabled,
                    CreatedAt = now,
                    UpdatedAt = now
                };
                db.CheckerAssignmentOverrides.Add(existing);
            }

            await db.SaveChangesAsync();
            return Results.Ok(ToCheckerResponse(existing));
        })
        .WithTags("Assignments")
        .WithName("UpsertCheckerOverride")
        .WithSummary("Create or update a group-level checker assignment override for a host. Returns 404 if host/assignment missing, 400 for a direct (non-group) assignment.")
        .Produces<OverrideResponse>()
        .ProducesProblem(StatusCodes.Status404NotFound)
        .ProducesProblem(StatusCodes.Status400BadRequest)
        .RequireAuthorization().RequirePermission(PermissionResource.Assignments);

        app.MapDelete("/api/hosts/{hostId:guid}/overrides/checkers/{assignmentId:guid}", async (
            Guid hostId, Guid assignmentId, MoneDbContext db) =>
        {
            var existing = await db.CheckerAssignmentOverrides
                .FirstOrDefaultAsync(o => o.HostId == hostId && o.CheckerAssignmentId == assignmentId);
            if (existing is null)
                return Results.Problem("Checker assignment override not found.", statusCode: StatusCodes.Status404NotFound);

            db.CheckerAssignmentOverrides.Remove(existing);
            await db.SaveChangesAsync();
            return Results.NoContent();
        })
        .WithTags("Assignments")
        .WithName("DeleteCheckerOverride")
        .WithSummary("Remove a checker assignment override for a host. Returns 404 if the override does not exist.")
        .Produces(StatusCodes.Status204NoContent)
        .ProducesProblem(StatusCodes.Status404NotFound)
        .RequireAuthorization().RequirePermission(PermissionResource.Assignments);

        app.MapGet("/api/hosts/{hostId:guid}/overrides", async (Guid hostId, MoneDbContext db) =>
        {
            if (!await db.Hosts.AnyAsync(h => h.Id == hostId))
                return Results.Problem("Host not found.", statusCode: StatusCodes.Status404NotFound);

            var probeOverrides = (await db.ProbeAssignmentOverrides
                .Where(o => o.HostId == hostId)
                .ToListAsync())
                .Select(ToProbeResponse)
                .ToList();

            var checkerOverrides = (await db.CheckerAssignmentOverrides
                .Where(o => o.HostId == hostId)
                .ToListAsync())
                .Select(ToCheckerResponse)
                .ToList();

            return Results.Ok(new HostOverridesResponse(probeOverrides, checkerOverrides));
        })
        .WithTags("Assignments")
        .WithName("GetHostOverrides")
        .WithSummary("List all probe and checker assignment overrides for a host. Returns 404 if the host does not exist.")
        .Produces<HostOverridesResponse>()
        .ProducesProblem(StatusCodes.Status404NotFound)
        .RequireAuthorization().RequirePermission(PermissionResource.Assignments);
    }

    private static OverrideResponse ToProbeResponse(ProbeAssignmentOverrideEntity e) => new()
    {
        Id = e.Id,
        HostId = e.HostId,
        AssignmentId = e.ProbeAssignmentId,
        ConfigJsonOverride = e.ConfigJsonOverride,
        IsDisabled = e.IsDisabled,
        CreatedAt = e.CreatedAt,
        UpdatedAt = e.UpdatedAt,
        Type = "probe"
    };

    private static OverrideResponse ToCheckerResponse(CheckerAssignmentOverrideEntity e) => new()
    {
        Id = e.Id,
        HostId = e.HostId,
        AssignmentId = e.CheckerAssignmentId,
        ConfigJsonOverride = e.ConfigJsonOverride,
        IsDisabled = e.IsDisabled,
        CreatedAt = e.CreatedAt,
        UpdatedAt = e.UpdatedAt,
        Type = "checker"
    };
}
