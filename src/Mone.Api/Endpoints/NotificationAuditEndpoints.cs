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
            .RequireAuthorization()
            .RequirePermission(PermissionResource.Monitoring);

        global.MapGet("/", async (
            Guid? targetId,
            string? pluginId,
            DeliveryStatus? status,
            DateTimeOffset? from,
            DateTimeOffset? to,
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
                .Select(n => new NotificationAuditResponse(
                    n.Id, n.Timestamp, n.TargetId, n.CheckerId,
                    n.NotificationPluginId, n.DeliveryStatus, n.ErrorMessage,
                    n.RetryCount, n.PreviousMonitoringStatus, n.CurrentMonitoringStatus))
                .ToListAsync();

            return Results.Ok(results);
        });

        var scoped = app.MapGroup("/api/hosts/{hostId:guid}/notifications")
            .RequireAuthorization()
            .RequirePermission(PermissionResource.Monitoring);

        scoped.MapGet("/", async (Guid hostId, MoneDbContext db) =>
        {
            if (!await db.Hosts.AnyAsync(h => h.Id == hostId))
                return Results.NotFound();

            var results = await db.NotificationAudit
                .Where(n => n.TargetId == hostId)
                .OrderByDescending(n => n.Timestamp)
                .Select(n => new NotificationAuditResponse(
                    n.Id, n.Timestamp, n.TargetId, n.CheckerId,
                    n.NotificationPluginId, n.DeliveryStatus, n.ErrorMessage,
                    n.RetryCount, n.PreviousMonitoringStatus, n.CurrentMonitoringStatus))
                .ToListAsync();

            return Results.Ok(results);
        });
    }
}
