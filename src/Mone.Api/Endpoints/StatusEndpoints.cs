using Microsoft.EntityFrameworkCore;
using Mone.Api.Models;
using Mone.Infrastructure.Data;

namespace Mone.Api.Endpoints;

public static class StatusEndpoints
{
    public static void MapStatusEndpoints(this WebApplication app)
    {
        var group = app.MapGroup("/api/hosts/{hostId:guid}/status").RequireAuthorization();

        group.MapGet("/latest", async (Guid hostId, MoneDbContext db) =>
        {
            if (!await db.Hosts.AnyAsync(h => h.Id == hostId))
                return Results.NotFound();

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
        });

        group.MapGet("/history", async (Guid hostId, UtcQueryTime? from, UtcQueryTime? to, MoneDbContext db) =>
        {
            if (!await db.Hosts.AnyAsync(h => h.Id == hostId))
                return Results.NotFound();

            IQueryable<Infrastructure.Data.Entities.StatusHistoryEntity> query = db.StatusHistory
                .Where(s => s.TargetId == hostId);

            if (from.HasValue) query = query.Where(s => s.Timestamp >= from.Value.Utc);
            if (to.HasValue) query = query.Where(s => s.Timestamp <= to.Value.Utc);

            var history = await query
                .OrderByDescending(s => s.Timestamp)
                .Select(s => new StatusResponse(s.Timestamp, s.TargetId, s.CheckerId, s.PreviousStatus, s.CurrentStatus))
                .ToListAsync();

            return Results.Ok(history);
        });
    }
}
