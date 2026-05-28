using Microsoft.EntityFrameworkCore;
using Mone.Api.Models;
using Mone.Infrastructure.Data;

namespace Mone.Api.Endpoints;

public static class ProbeResultEndpoints
{
    public static void MapProbeResultEndpoints(this WebApplication app)
    {
        var group = app.MapGroup("/api/hosts/{hostId:guid}/results").RequireAuthorization();

        group.MapGet("/", async (Guid hostId, DateTimeOffset? from, DateTimeOffset? to, string? probeId, MoneDbContext db) =>
        {
            if (!await db.Hosts.AnyAsync(h => h.Id == hostId))
                return Results.NotFound();

            IQueryable<Infrastructure.Data.Entities.ProbeResultEntity> query = db.ProbeResults
                .Where(r => r.TargetId == hostId);

            if (from.HasValue) query = query.Where(r => r.Timestamp >= from.Value);
            if (to.HasValue) query = query.Where(r => r.Timestamp <= to.Value);
            if (!string.IsNullOrWhiteSpace(probeId)) query = query.Where(r => r.ProbeId == probeId);

            var results = await query
                .OrderByDescending(r => r.Timestamp)
                .Select(r => new ProbeResultResponse(r.Timestamp, r.TargetId, r.ProbeId, r.Status, r.Summary, r.DurationMs, r.MetadataJson))
                .ToListAsync();

            return Results.Ok(results);
        });

        group.MapGet("/metric-keys", async (Guid hostId, MoneDbContext db) =>
        {
            if (!await db.Hosts.AnyAsync(h => h.Id == hostId))
                return Results.NotFound();

            var recentMetadata = await db.ProbeResults
                .Where(r => r.TargetId == hostId && r.MetadataJson != null)
                .OrderByDescending(r => r.Timestamp)
                .Select(r => r.MetadataJson!)
                .Take(200)
                .ToListAsync();

            var keys = recentMetadata
                .SelectMany(json =>
                {
                    try
                    {
                        var dict = System.Text.Json.JsonSerializer.Deserialize<Dictionary<string, object>>(json);
                        return dict?.Keys.AsEnumerable() ?? Enumerable.Empty<string>();
                    }
                    catch { return []; }
                })
                .Distinct()
                .Order()
                .ToArray();

            return Results.Ok(keys);
        });

        group.MapGet("/latest-per-probe", async (Guid hostId, MoneDbContext db) =>
        {
            if (!await db.Hosts.AnyAsync(h => h.Id == hostId))
                return Results.NotFound();

            var probeIds = await db.ProbeResults
                .Where(r => r.TargetId == hostId)
                .Select(r => r.ProbeId)
                .Distinct()
                .ToListAsync();

            var results = new List<ProbeResultResponse>();
            foreach (var pid in probeIds)
            {
                var latest = await db.ProbeResults
                    .Where(r => r.TargetId == hostId && r.ProbeId == pid)
                    .OrderByDescending(r => r.Timestamp)
                    .Select(r => new ProbeResultResponse(r.Timestamp, r.TargetId, r.ProbeId, r.Status, r.Summary, r.DurationMs, r.MetadataJson))
                    .FirstOrDefaultAsync();
                if (latest is not null)
                    results.Add(latest);
            }

            return Results.Ok(results);
        });
    }
}
