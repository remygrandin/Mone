using Microsoft.EntityFrameworkCore;
using Mone.Api.Models;
using Mone.Infrastructure.Data;

namespace Mone.Api.Endpoints;

public static class HousekeepingEndpoints
{
    private static readonly TimeSpan OneWeek = TimeSpan.FromDays(7);
    private static readonly TimeSpan OneMonth = TimeSpan.FromDays(30);
    private static readonly TimeSpan SixMonths = TimeSpan.FromDays(180);
    private static readonly TimeSpan OneYear = TimeSpan.FromDays(365);

    public static void MapHousekeepingEndpoints(this WebApplication app)
    {
        var group = app.MapGroup("/api/housekeeping").RequireAuthorization();

        group.MapGet("/db-size", async (MoneDbContext db) =>
            Results.Ok(await GetDbSizeAsync(db)));

        group.MapPost("/assess", async (
            MoneDbContext db,
            Mone.PluginEngine.PluginEngine engine) =>
        {
            var installedIds = engine.Registry.GetAll()
                .Select(r => r.Metadata.PluginId)
                .ToHashSet(StringComparer.OrdinalIgnoreCase)
                .ToArray();

            var dbSize = await GetDbSizeAsync(db);
            var avgRowBytes = ComputeAverageRowBytes(dbSize);

            var categories = await BuildCategoriesAsync(db, installedIds, avgRowBytes);
            return Results.Ok(new HousekeepingReport(dbSize, categories));
        });

        group.MapPost("/cleanup", async (
            CleanupRequest request,
            MoneDbContext db,
            Mone.PluginEngine.PluginEngine engine,
            ILoggerFactory loggerFactory) =>
        {
            var logger = loggerFactory.CreateLogger("Mone.Api.Housekeeping");
            var installedIds = engine.Registry.GetAll()
                .Select(r => r.Metadata.PluginId)
                .ToHashSet(StringComparer.OrdinalIgnoreCase)
                .ToArray();

            try
            {
                var deleted = await CleanupAsync(db, request.Key, installedIds);
                logger.LogInformation("Housekeeping cleanup '{Key}' deleted {Rows} rows", request.Key, deleted);
                return Results.Ok(new CleanupResult(request.Key, deleted));
            }
            catch (KeyNotFoundException)
            {
                return Results.BadRequest(new { error = $"Unknown cleanup key '{request.Key}'" });
            }
        });
    }

    private static async Task<DbSizeReport> GetDbSizeAsync(MoneDbContext db)
    {
        var conn = db.Database.GetDbConnection();
        await conn.OpenAsync();
        try
        {
            long totalBytes;
            string totalPretty;
            using (var cmd = conn.CreateCommand())
            {
                cmd.CommandText = "SELECT pg_database_size(current_database()), pg_size_pretty(pg_database_size(current_database()))";
                using var reader = await cmd.ExecuteReaderAsync();
                await reader.ReadAsync();
                totalBytes = reader.GetInt64(0);
                totalPretty = reader.GetString(1);
            }

            var tables = new List<TableSize>();
            using (var cmd = conn.CreateCommand())
            {
                cmd.CommandText = @"
                    SELECT t.name, t.bytes, pg_size_pretty(t.bytes) AS pretty
                    FROM (
                        SELECT c.relname AS name, pg_total_relation_size(c.oid) AS bytes
                        FROM pg_class c
                        JOIN pg_namespace n ON n.oid = c.relnamespace
                        WHERE c.relkind = 'r'
                          AND n.nspname = 'public'
                          AND c.relname NOT IN (
                              SELECT hypertable_name FROM timescaledb_information.hypertables
                          )
                        UNION ALL
                        SELECT hypertable_name AS name,
                               hypertable_size(format('%I.%I', hypertable_schema, hypertable_name)::regclass) AS bytes
                        FROM timescaledb_information.hypertables
                    ) t
                    WHERE t.bytes > 0
                    ORDER BY t.bytes DESC
                    LIMIT 30";
                using var reader = await cmd.ExecuteReaderAsync();
                while (await reader.ReadAsync())
                    tables.Add(new TableSize(reader.GetString(0), reader.GetInt64(1), reader.GetString(2)));
            }

            return new DbSizeReport(totalBytes, totalPretty, tables);
        }
        finally
        {
            await conn.CloseAsync();
        }
    }

    private static Dictionary<string, double> ComputeAverageRowBytes(DbSizeReport dbSize)
    {
        return dbSize.Tables.ToDictionary(t => t.Name, t => (double)t.Bytes, StringComparer.OrdinalIgnoreCase);
    }

    private static async Task<List<HousekeepingCategory>> BuildCategoriesAsync(
        MoneDbContext db, string[] installedIds, Dictionary<string, double> tableBytes)
    {
        var now = DateTimeOffset.UtcNow;
        var list = new List<HousekeepingCategory>();

        var totalProbeResults = await db.ProbeResults.LongCountAsync();
        var totalStatusHistory = await db.StatusHistory.LongCountAsync();
        var totalNotificationAudit = await db.NotificationAudit.LongCountAsync();

        long? EstSize(string table, long matching, long total)
        {
            if (matching <= 0 || total <= 0) return null;
            if (!tableBytes.TryGetValue(table, out var bytes) || bytes <= 0) return null;
            return (long)(bytes * matching / (double)total);
        }

        // probe_results — orphaned by uninstalled plugins
        {
            var n = await db.ProbeResults
                .Where(x => !installedIds.Contains(x.ProbeId))
                .LongCountAsync();
            var bytes = EstSize("probe_results", n, totalProbeResults);
            list.Add(MakeCat("orphan-probe-results",
                "Probe results from uninstalled plugins", "Orphaned by plugin", n, bytes));
        }

        // probe_results — old
        foreach (var (label, ts, key) in new[]
        {
            ("Probe results older than 1 week", now - OneWeek, "old-probe-results-1w"),
            ("Probe results older than 1 month", now - OneMonth, "old-probe-results-1m"),
            ("Probe results older than 6 months", now - SixMonths, "old-probe-results-6m"),
            ("Probe results older than 1 year", now - OneYear, "old-probe-results-1y"),
        })
        {
            var n = await db.ProbeResults.Where(x => x.Timestamp < ts).LongCountAsync();
            list.Add(MakeCat(key, label, "Old time-series data", n, EstSize("probe_results", n, totalProbeResults)));
        }

        // status_history — orphaned by uninstalled checkers
        {
            var n = await db.StatusHistory
                .Where(x => !installedIds.Contains(x.CheckerId))
                .LongCountAsync();
            list.Add(MakeCat("orphan-status-history",
                "Status history from uninstalled checkers", "Orphaned by plugin", n,
                EstSize("status_history", n, totalStatusHistory)));
        }

        // status_history — old
        foreach (var (label, ts, key) in new[]
        {
            ("Status history older than 1 week", now - OneWeek, "old-status-history-1w"),
            ("Status history older than 1 month", now - OneMonth, "old-status-history-1m"),
            ("Status history older than 6 months", now - SixMonths, "old-status-history-6m"),
            ("Status history older than 1 year", now - OneYear, "old-status-history-1y"),
        })
        {
            var n = await db.StatusHistory.Where(x => x.Timestamp < ts).LongCountAsync();
            list.Add(MakeCat(key, label, "Old time-series data", n, EstSize("status_history", n, totalStatusHistory)));
        }

        // notification_audit — orphaned by uninstalled notification plugins
        {
            var n = await db.NotificationAudit
                .Where(x => !installedIds.Contains(x.NotificationPluginId))
                .LongCountAsync();
            list.Add(MakeCat("orphan-notification-audit",
                "Notification audit from uninstalled plugins", "Orphaned by plugin", n,
                EstSize("notification_audit", n, totalNotificationAudit)));
        }

        // notification_audit — old
        foreach (var (label, ts, key) in new[]
        {
            ("Notification audit older than 1 week", now - OneWeek, "old-notification-audit-1w"),
            ("Notification audit older than 1 month", now - OneMonth, "old-notification-audit-1m"),
            ("Notification audit older than 6 months", now - SixMonths, "old-notification-audit-6m"),
            ("Notification audit older than 1 year", now - OneYear, "old-notification-audit-1y"),
        })
        {
            var n = await db.NotificationAudit.Where(x => x.Timestamp < ts).LongCountAsync();
            list.Add(MakeCat(key, label, "Old time-series data", n,
                EstSize("notification_audit", n, totalNotificationAudit)));
        }

        // disabled hosts — by age
        foreach (var (label, ts, key) in new[]
        {
            ("Disabled hosts (inactive >1 month)", now - OneMonth, "disabled-hosts-1m"),
            ("Disabled hosts (inactive >6 months)", now - SixMonths, "disabled-hosts-6m"),
            ("Disabled hosts (inactive >1 year)", now - OneYear, "disabled-hosts-1y"),
        })
        {
            var n = await db.Hosts.Where(h => !h.Enabled && h.UpdatedAt < ts).LongCountAsync();
            list.Add(MakeCat(key, label, "Stale hosts", n, null));
        }

        // Orphan assignments / configs by plugin
        {
            var n = await db.ProbeAssignments
                .Where(x => !installedIds.Contains(x.ProbePluginId))
                .LongCountAsync();
            list.Add(MakeCat("orphan-probe-assignments",
                "Probe assignments referencing uninstalled plugins", "Orphaned by plugin", n, null));
        }
        {
            var n = await db.CheckerAssignments
                .Where(x => !installedIds.Contains(x.CheckerPluginId))
                .LongCountAsync();
            list.Add(MakeCat("orphan-checker-assignments",
                "Checker assignments referencing uninstalled plugins", "Orphaned by plugin", n, null));
        }
        {
            var n = await db.NotificationConfigs
                .Where(x => !installedIds.Contains(x.PluginId))
                .LongCountAsync();
            list.Add(MakeCat("orphan-notification-configs",
                "Notification configs referencing uninstalled plugins", "Orphaned by plugin", n, null));
        }
        {
            var n = await db.PluginGlobalConfigs
                .Where(x => !installedIds.Contains(x.PluginId))
                .LongCountAsync();
            list.Add(MakeCat("orphan-plugin-global-configs",
                "Plugin global configs for uninstalled plugins", "Orphaned by plugin", n, null));
        }

        // Misc cleanables
        {
            var n = await db.Tags.Where(t => !t.HostTags.Any()).LongCountAsync();
            list.Add(MakeCat("unused-tags", "Tags with no host", "Empty references", n, null));
        }
        {
            var n = await db.HostGroups.Where(g => !g.GroupMemberships.Any()).LongCountAsync();
            list.Add(MakeCat("empty-host-groups", "Host groups with no members", "Empty references", n, null));
        }

        return list;
    }

    private static HousekeepingCategory MakeCat(string key, string label, string group, long count, long? bytes)
        => new(key, label, group, count, bytes, bytes.HasValue ? FormatBytes(bytes.Value) : null);

    private static string FormatBytes(long bytes)
    {
        if (bytes < 1024) return $"{bytes} B";
        double v = bytes;
        string[] u = ["KB", "MB", "GB", "TB"];
        int i = -1;
        do { v /= 1024; i++; } while (v >= 1024 && i < u.Length - 1);
        return $"{v:0.##} {u[i]}";
    }

    private static async Task<long> CleanupAsync(MoneDbContext db, string key, string[] installedIds)
    {
        var now = DateTimeOffset.UtcNow;

        return key switch
        {
            "orphan-probe-results" => await db.ProbeResults
                .Where(x => !installedIds.Contains(x.ProbeId)).ExecuteDeleteAsync(),
            "old-probe-results-1w" => await db.ProbeResults.Where(x => x.Timestamp < now - OneWeek).ExecuteDeleteAsync(),
            "old-probe-results-1m" => await db.ProbeResults.Where(x => x.Timestamp < now - OneMonth).ExecuteDeleteAsync(),
            "old-probe-results-6m" => await db.ProbeResults.Where(x => x.Timestamp < now - SixMonths).ExecuteDeleteAsync(),
            "old-probe-results-1y" => await db.ProbeResults.Where(x => x.Timestamp < now - OneYear).ExecuteDeleteAsync(),

            "orphan-status-history" => await db.StatusHistory
                .Where(x => !installedIds.Contains(x.CheckerId)).ExecuteDeleteAsync(),
            "old-status-history-1w" => await db.StatusHistory.Where(x => x.Timestamp < now - OneWeek).ExecuteDeleteAsync(),
            "old-status-history-1m" => await db.StatusHistory.Where(x => x.Timestamp < now - OneMonth).ExecuteDeleteAsync(),
            "old-status-history-6m" => await db.StatusHistory.Where(x => x.Timestamp < now - SixMonths).ExecuteDeleteAsync(),
            "old-status-history-1y" => await db.StatusHistory.Where(x => x.Timestamp < now - OneYear).ExecuteDeleteAsync(),

            "orphan-notification-audit" => await db.NotificationAudit
                .Where(x => !installedIds.Contains(x.NotificationPluginId)).ExecuteDeleteAsync(),
            "old-notification-audit-1w" => await db.NotificationAudit.Where(x => x.Timestamp < now - OneWeek).ExecuteDeleteAsync(),
            "old-notification-audit-1m" => await db.NotificationAudit.Where(x => x.Timestamp < now - OneMonth).ExecuteDeleteAsync(),
            "old-notification-audit-6m" => await db.NotificationAudit.Where(x => x.Timestamp < now - SixMonths).ExecuteDeleteAsync(),
            "old-notification-audit-1y" => await db.NotificationAudit.Where(x => x.Timestamp < now - OneYear).ExecuteDeleteAsync(),

            "disabled-hosts-1m" => await db.Hosts.Where(h => !h.Enabled && h.UpdatedAt < now - OneMonth).ExecuteDeleteAsync(),
            "disabled-hosts-6m" => await db.Hosts.Where(h => !h.Enabled && h.UpdatedAt < now - SixMonths).ExecuteDeleteAsync(),
            "disabled-hosts-1y" => await db.Hosts.Where(h => !h.Enabled && h.UpdatedAt < now - OneYear).ExecuteDeleteAsync(),

            "orphan-probe-assignments" => await db.ProbeAssignments
                .Where(x => !installedIds.Contains(x.ProbePluginId)).ExecuteDeleteAsync(),
            "orphan-checker-assignments" => await db.CheckerAssignments
                .Where(x => !installedIds.Contains(x.CheckerPluginId)).ExecuteDeleteAsync(),
            "orphan-notification-configs" => await db.NotificationConfigs
                .Where(x => !installedIds.Contains(x.PluginId)).ExecuteDeleteAsync(),
            "orphan-plugin-global-configs" => await db.PluginGlobalConfigs
                .Where(x => !installedIds.Contains(x.PluginId)).ExecuteDeleteAsync(),

            "unused-tags" => await db.Tags.Where(t => !t.HostTags.Any()).ExecuteDeleteAsync(),
            "empty-host-groups" => await db.HostGroups.Where(g => !g.GroupMemberships.Any()).ExecuteDeleteAsync(),

            _ => throw new KeyNotFoundException(key),
        };
    }
}
