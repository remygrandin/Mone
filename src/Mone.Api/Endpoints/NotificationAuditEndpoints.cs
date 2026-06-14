using Microsoft.EntityFrameworkCore;
using Mone.Api.Models;
using Mone.Infrastructure.Data;
using Mone.Infrastructure.Data.Entities;

using Mone.Api.Authorization;
using Mone.Contracts.Models;

namespace Mone.Api.Endpoints;

public static class NotificationAuditEndpoints
{
    public static void MapNotificationAuditEndpoints(this WebApplication app)
    {
        var global = app.MapGroup("/api/notifications")
            .WithTags("Notifications")
            .RequireAuthorization()
            .RequirePermission(PermissionResource.Monitoring);

        global.MapGet("/", async (
            Guid? targetId,
            string? pluginId,
            DeliveryStatus? status,
            DateTimeOffset? from,
            DateTimeOffset? to,
            int? limit,
            MoneDbContext db) =>
        {
            IQueryable<NotificationAuditEntity> query = db.NotificationAudit;

            if (targetId.HasValue) query = query.Where(n => n.TargetId == targetId.Value);
            if (pluginId is not null) query = query.Where(n => n.NotificationPluginId == pluginId);
            if (status.HasValue) query = query.Where(n => n.DeliveryStatus == status.Value);
            if (from.HasValue) query = query.Where(n => n.Timestamp >= from.Value);
            if (to.HasValue) query = query.Where(n => n.Timestamp <= to.Value);

            var results = await query
                .OrderByDescending(n => n.Timestamp)
                .Take(Math.Clamp(limit ?? 500, 1, 2000))
                .Select(n => new NotificationAuditResponse(
                    n.Id, n.Timestamp, n.TargetId, n.CheckerId,
                    n.NotificationPluginId, n.DeliveryStatus, n.ErrorMessage,
                    n.RetryCount, n.PreviousMonitoringStatus, n.CurrentMonitoringStatus))
                .ToListAsync();

            return Results.Ok(results);
        })
        .WithName("ListNotificationAudit")
        .WithSummary("List notification delivery audit records, filterable by target, plugin, status, and time range. Newest-first, bounded by the limit query param (default 500, max 2000 rows).")
        .Produces<IEnumerable<NotificationAuditResponse>>();

        var scoped = app.MapGroup("/api/hosts/{hostId:guid}/notifications")
            .WithTags("Notifications")
            .RequireAuthorization()
            .RequirePermission(PermissionResource.Monitoring);

        scoped.MapGet("/", async (Guid hostId, int? limit, MoneDbContext db) =>
        {
            if (!await db.Hosts.AnyAsync(h => h.Id == hostId))
                return Results.Problem("Host not found.", statusCode: StatusCodes.Status404NotFound);

            var results = await db.NotificationAudit
                .Where(n => n.TargetId == hostId)
                .OrderByDescending(n => n.Timestamp)
                .Take(Math.Clamp(limit ?? 500, 1, 2000))
                .Select(n => new NotificationAuditResponse(
                    n.Id, n.Timestamp, n.TargetId, n.CheckerId,
                    n.NotificationPluginId, n.DeliveryStatus, n.ErrorMessage,
                    n.RetryCount, n.PreviousMonitoringStatus, n.CurrentMonitoringStatus))
                .ToListAsync();

            return Results.Ok(results);
        })
        .WithName("ListHostNotificationAudit")
        .WithSummary("List notification delivery audit records for a single host. Newest-first, bounded by the limit query param (default 500, max 2000 rows). Returns 404 if the host does not exist.")
        .Produces<IEnumerable<NotificationAuditResponse>>()
        .ProducesProblem(StatusCodes.Status404NotFound);
    }
}
