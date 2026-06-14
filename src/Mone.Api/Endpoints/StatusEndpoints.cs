using Microsoft.EntityFrameworkCore;
using Mone.Api.Models;
using Mone.Infrastructure.Data;

using Mone.Api.Authorization;
using Mone.Contracts.Models;

namespace Mone.Api.Endpoints;

public static class StatusEndpoints
{
    public static void MapStatusEndpoints(this WebApplication app)
    {
        var group = app.MapGroup("/api/hosts/{hostId:guid}/status")
            .WithTags("Status")
            .RequireAuthorization()
            .RequirePermission(PermissionResource.Monitoring);

        group.MapGet("/latest", async (Guid hostId, MoneDbContext db) =>
        {
            if (!await db.Hosts.AnyAsync(h => h.Id == hostId))
                return Results.Problem("Host not found.", statusCode: StatusCodes.Status404NotFound);

            var checkerIds = await db.StatusHistory
                .Where(s => s.TargetId == hostId)
                .Select(s => s.CheckerId)
                .Distinct()
                .ToListAsync();

            var latest = new List<StatusResponse>();
            foreach (var checkerId in checkerIds)
            {
                var entry = await db.StatusHistory
                    .Where(s => s.TargetId == hostId && s.CheckerId == checkerId)
                    .OrderByDescending(s => s.Timestamp)
                    .Select(s => new StatusResponse(s.Timestamp, s.TargetId, s.CheckerId, s.PreviousStatus, s.CurrentStatus))
                    .FirstAsync();
                latest.Add(entry);
            }

            return Results.Ok(latest);
        })
        .WithName("GetLatestStatus")
        .WithSummary("Get the latest status per checker for a host. Returns 404 if the host does not exist.")
        .Produces<IEnumerable<StatusResponse>>()
        .ProducesProblem(StatusCodes.Status404NotFound);

        group.MapGet("/history", async (Guid hostId, UtcQueryTime? from, UtcQueryTime? to, int? limit, MoneDbContext db) =>
        {
            if (!await db.Hosts.AnyAsync(h => h.Id == hostId))
                return Results.Problem("Host not found.", statusCode: StatusCodes.Status404NotFound);

            IQueryable<Infrastructure.Data.Entities.StatusHistoryEntity> query = db.StatusHistory
                .Where(s => s.TargetId == hostId);

            if (from.HasValue) query = query.Where(s => s.Timestamp >= from.Value.Utc);
            if (to.HasValue) query = query.Where(s => s.Timestamp <= to.Value.Utc);

            var history = await query
                .OrderByDescending(s => s.Timestamp)
                .Take(Math.Clamp(limit ?? 1000, 1, 5000))
                .Select(s => new StatusResponse(s.Timestamp, s.TargetId, s.CheckerId, s.PreviousStatus, s.CurrentStatus))
                .ToListAsync();

            return Results.Ok(history);
        })
        .WithName("GetStatusHistory")
        .WithSummary("Get status-transition history for a host within an optional time range. Newest-first, bounded by the limit query param (default 1000, max 5000 rows). Returns 404 if the host does not exist.")
        .Produces<IEnumerable<StatusResponse>>()
        .ProducesProblem(StatusCodes.Status404NotFound);
    }
}
