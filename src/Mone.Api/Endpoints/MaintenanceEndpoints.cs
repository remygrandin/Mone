using Cronos;
using Microsoft.EntityFrameworkCore;
using Mone.Api.Authorization;
using Mone.Api.Models;
using Mone.Contracts.Models;
using Mone.Infrastructure.Data;
using Mone.Infrastructure.Data.Entities;

namespace Mone.Api.Endpoints;

public static class MaintenanceEndpoints
{
    public static void MapMaintenanceEndpoints(this WebApplication app)
    {
        var group = app.MapGroup("/api/hosts/{hostId:guid}/maintenance")
            .WithTags("Maintenance")
            .RequireAuthorization()
            .RequirePermission(PermissionResource.Hosts);

        group.MapGet("/", async (Guid hostId, MoneDbContext db) =>
        {
            if (!await db.Hosts.AnyAsync(h => h.Id == hostId))
                return Results.Problem("Host not found.", statusCode: StatusCodes.Status404NotFound);

            var windows = await db.MaintenanceWindows
                .Where(w => w.HostId == hostId)
                .OrderByDescending(w => w.CreatedAt)
                .ToListAsync();

            return Results.Ok(windows.Select(ToResponse));
        })
        .WithName("ListMaintenanceWindows")
        .WithSummary("List maintenance windows for a host. Returns 404 if the host does not exist.")
        .Produces<IEnumerable<MaintenanceWindowResponse>>()
        .ProducesProblem(StatusCodes.Status404NotFound);

        group.MapPost("/", async (Guid hostId, CreateMaintenanceWindowRequest request, MoneDbContext db) =>
        {
            if (!await db.Hosts.AnyAsync(h => h.Id == hostId))
                return Results.Problem("Host not found.", statusCode: StatusCodes.Status404NotFound);

            var now = DateTimeOffset.UtcNow;
            var window = new MaintenanceWindowEntity
            {
                Id = Guid.NewGuid(),
                HostId = hostId,
                Kind = request.Kind,
                DurationMinutes = request.DurationMinutes,
                Enabled = true,
                CreatedAt = now,
                UpdatedAt = now
            };

            if (request.Kind == MaintenanceWindowKind.RecurringCron)
            {
                if (string.IsNullOrWhiteSpace(request.Cron))
                    return Results.ValidationProblem(new Dictionary<string, string[]>
                    {
                        ["Cron"] = new[] { "Cron is required for a recurring window." }
                    });

                try
                {
                    CronExpression.Parse(request.Cron);
                }
                catch (CronFormatException)
                {
                    return Results.ValidationProblem(new Dictionary<string, string[]>
                    {
                        ["Cron"] = new[] { "Invalid cron expression." }
                    });
                }

                if (request.DurationMinutes < 1)
                    return Results.ValidationProblem(new Dictionary<string, string[]>
                    {
                        ["DurationMinutes"] = new[] { "DurationMinutes must be at least 1 for a recurring window." }
                    });

                window.Cron = request.Cron;
                window.StartsAt = null;
                window.ExpiresAt = null;
            }
            else
            {
                var startsAt = request.StartsAt ?? now;
                window.StartsAt = startsAt;
                window.ExpiresAt = request.DurationMinutes > 0
                    ? startsAt.AddMinutes(request.DurationMinutes)
                    : null;
            }

            db.MaintenanceWindows.Add(window);
            await db.SaveChangesAsync();

            return Results.Created($"/api/hosts/{hostId}/maintenance/{window.Id}", ToResponse(window));
        })
        .WithName("CreateMaintenanceWindow")
        .WithSummary("Create a manual or recurring-cron maintenance window for a host. Returns 404 if the host does not exist, 400 on an invalid cron or duration.")
        .Produces<MaintenanceWindowResponse>(StatusCodes.Status201Created)
        .ProducesValidationProblem()
        .ProducesProblem(StatusCodes.Status404NotFound);

        group.MapDelete("/{windowId:guid}", async (Guid hostId, Guid windowId, MoneDbContext db) =>
        {
            var window = await db.MaintenanceWindows
                .FirstOrDefaultAsync(w => w.Id == windowId && w.HostId == hostId);
            if (window is null)
                return Results.Problem("Maintenance window not found.", statusCode: StatusCodes.Status404NotFound);

            db.MaintenanceWindows.Remove(window);
            await db.SaveChangesAsync();
            return Results.NoContent();
        })
        .WithName("DeleteMaintenanceWindow")
        .WithSummary("Delete a maintenance window by id. Returns 404 if the window does not exist for the host.")
        .Produces(StatusCodes.Status204NoContent)
        .ProducesProblem(StatusCodes.Status404NotFound);
    }

    private static MaintenanceWindowResponse ToResponse(MaintenanceWindowEntity w) => new(
        w.Id,
        w.HostId,
        w.Kind,
        w.StartsAt,
        w.ExpiresAt,
        w.Cron,
        w.DurationMinutes,
        w.Enabled);
}
