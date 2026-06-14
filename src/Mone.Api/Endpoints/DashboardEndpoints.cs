using Microsoft.EntityFrameworkCore;
using Mone.Api.Models;
using Mone.Contracts.Models;
using Mone.Infrastructure.Data;

using Mone.Api.Authorization;

namespace Mone.Api.Endpoints;

public static class DashboardEndpoints
{
    public static void MapDashboardEndpoints(this WebApplication app)
    {
        var group = app.MapGroup("/api/dashboard")
            .WithTags("Dashboard")
            .RequireAuthorization()
            .RequirePermission(PermissionResource.Monitoring);

        group.MapGet("/summary", async (MoneDbContext db) =>
        {
            var hostIds = await db.Hosts
                .Where(h => h.Enabled)
                .Select(h => h.Id)
                .ToListAsync();

            var totalHosts = hostIds.Count;

            if (totalHosts == 0)
            {
                return Results.Ok(new DashboardSummaryResponse(0, 0, 0, 0, 0, 0));
            }

            var latestStatuses = await db.StatusHistory
                .Where(s => hostIds.Contains(s.TargetId))
                .GroupBy(s => new { s.TargetId, s.CheckerId })
                .Select(g => g.OrderByDescending(s => s.Timestamp).First())
                .ToListAsync();

            var worstPerHost = latestStatuses
                .GroupBy(s => s.TargetId)
                .ToDictionary(
                    g => g.Key,
                    g => g.Max(s => StatusSeverity(s.CurrentStatus)));

            int healthy = 0, degraded = 0, unhealthy = 0, unreachable = 0, unknown = 0;

            foreach (var hostId in hostIds)
            {
                var severity = worstPerHost.GetValueOrDefault(hostId, StatusSeverity(MonitoringStatus.Unknown));
                switch (severity)
                {
                    case 0: healthy++; break;
                    case 1: degraded++; break;
                    case 2: unhealthy++; break;
                    case 3: unreachable++; break;
                    default: unknown++; break;
                }
            }

            return Results.Ok(new DashboardSummaryResponse(totalHosts, healthy, degraded, unhealthy, unreachable, unknown));
        })
        .WithName("GetDashboardSummary")
        .WithSummary("Get aggregate host-health counts (healthy, degraded, unhealthy, unreachable, unknown) across all enabled hosts.")
        .Produces<DashboardSummaryResponse>();
    }

    private static int StatusSeverity(MonitoringStatus status) => status switch
    {
        MonitoringStatus.Healthy => 0,
        MonitoringStatus.Degraded => 1,
        MonitoringStatus.Unhealthy => 2,
        MonitoringStatus.Unreachable => 3,
        _ => -1 // Unknown maps to lowest severity but distinct bucket
    };
}
