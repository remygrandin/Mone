using Microsoft.EntityFrameworkCore;
using Mone.Api.Models;
using Mone.Infrastructure.Data;
using Mone.Infrastructure.Services;

using Mone.Api.Authorization;
using Mone.Contracts.Models;

namespace Mone.Api.Endpoints;

public static class ProbeResultEndpoints
{
    public static void MapProbeResultEndpoints(this WebApplication app)
    {
        var group = app.MapGroup("/api/hosts/{hostId:guid}/results")
            .RequireAuthorization()
            .RequirePermission(PermissionResource.Monitoring);

        group.MapGet("/", async (Guid hostId, UtcQueryTime? from, UtcQueryTime? to, string? probeId, MoneDbContext db) =>
        {
            if (!await db.Hosts.AnyAsync(h => h.Id == hostId))
                return Results.NotFound();

            IQueryable<Infrastructure.Data.Entities.ProbeResultEntity> query = db.ProbeResults
                .Where(r => r.TargetId == hostId);

            if (from.HasValue) query = query.Where(r => r.Timestamp >= from.Value.Utc);
            if (to.HasValue) query = query.Where(r => r.Timestamp <= to.Value.Utc);
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

        group.MapGet("/declared-metrics", async (Guid hostId, MoneDbContext db, InheritanceResolver resolver) =>
        {
            if (!await db.Hosts.AnyAsync(h => h.Id == hostId))
                return Results.NotFound();

            var effective = await resolver.GetEffectiveProbeAssignmentsAsync(hostId);
            var assignmentIds = effective.Select(e => e.AssignmentId).ToArray();
            if (assignmentIds.Length == 0)
                return Results.Ok(Array.Empty<HostDeclaredMetricResponse>());

            var rows = await (
                from m in db.ProbeAssignmentMetrics
                join a in db.ProbeAssignments on m.ProbeAssignmentId equals a.Id
                where assignmentIds.Contains(m.ProbeAssignmentId)
                select new HostDeclaredMetricResponse(
                    m.FullKey,
                    m.DisplayName,
                    m.Unit,
                    m.ValueMappingJson,
                    a.Id,
                    a.Name,
                    a.ProbePluginId)
            ).ToListAsync();

            var dedup = rows
                .GroupBy(r => r.FullKey, StringComparer.Ordinal)
                .Select(g => g.First())
                .OrderBy(r => r.FullKey, StringComparer.Ordinal)
                .ToArray();

            return Results.Ok(dedup);
        });

        group.MapGet("/metrics/series", async (Guid hostId, string key, int? points, UtcQueryTime? from, UtcQueryTime? to, MoneDbContext db) =>
        {
            if (string.IsNullOrWhiteSpace(key))
                return Results.BadRequest("key required");
            if (!await db.Hosts.AnyAsync(h => h.Id == hostId))
                return Results.NotFound();

            var hasRange = from.HasValue || to.HasValue;
            // A bounded window can legitimately contain many more points than the dashboard's
            // rolling view, so lift the cap when a date range is supplied.
            var maxPoints = hasRange
                ? Math.Clamp(points ?? 5000, 1, 5000)
                : Math.Clamp(points ?? 60, 1, 500);

            // Server-side extraction using JSONB path. Picks the most recent N rows that contain
            // a numeric-coercible value at the given key, regardless of how sparse the metric is.
            var sqlParams = new List<object> { key, hostId };
            var rangeClause = "";
            if (from.HasValue)
            {
                rangeClause += " AND \"Timestamp\" >= {" + sqlParams.Count + "}";
                sqlParams.Add(from.Value.Utc);
            }
            if (to.HasValue)
            {
                rangeClause += " AND \"Timestamp\" <= {" + sqlParams.Count + "}";
                sqlParams.Add(to.Value.Utc);
            }
            var limitToken = "{" + sqlParams.Count + "}";
            sqlParams.Add(maxPoints);

            // Single braces are literal in a $$ raw string; {{ }} mark interpolation holes.
            var sql =
                $$"""
                SELECT "Timestamp" AS "Timestamp",
                       CASE
                           WHEN jsonb_typeof("MetadataJson" -> {0}) = 'number'
                               THEN ("MetadataJson" ->> {0})::double precision
                           WHEN jsonb_typeof("MetadataJson" -> {0}) = 'boolean'
                               THEN CASE WHEN ("MetadataJson" -> {0})::text = 'true' THEN 1.0 ELSE 0.0 END
                           WHEN jsonb_typeof("MetadataJson" -> {0}) = 'string'
                               AND ("MetadataJson" ->> {0}) ~ '^-?[0-9]+(\.[0-9]+)?$'
                               THEN ("MetadataJson" ->> {0})::double precision
                           ELSE NULL
                       END AS "Value"
                FROM probe_results
                WHERE "TargetId" = {1}
                  AND "MetadataJson" IS NOT NULL
                  AND "MetadataJson" ? {0}{{rangeClause}}
                ORDER BY "Timestamp" DESC
                LIMIT {{limitToken}}
                """;

            var rows = await db.Database.SqlQueryRaw<JsonbMetricRow>(sql, sqlParams.ToArray()).ToListAsync();

            var pointsList = rows
                .Where(r => r.Value.HasValue)
                .Select(r => new MetricSeriesPoint(r.Timestamp, r.Value!.Value))
                .Reverse()
                .ToArray();

            double? min = null, max = null, avg = null, latest = null;
            DateTimeOffset? latestTs = null;
            if (pointsList.Length > 0)
            {
                min = pointsList.Min(p => p.Value);
                max = pointsList.Max(p => p.Value);
                avg = pointsList.Average(p => p.Value);
                latest = pointsList[^1].Value;
                latestTs = pointsList[^1].Timestamp;
            }

            return Results.Ok(new MetricSeriesResponse(key, pointsList.Length, min, max, avg, latest, latestTs, pointsList));
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

    private sealed class JsonbMetricRow
    {
        public DateTimeOffset Timestamp { get; set; }
        public double? Value { get; set; }
    }
}
