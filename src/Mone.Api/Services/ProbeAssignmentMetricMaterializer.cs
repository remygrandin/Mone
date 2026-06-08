using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using Mone.Contracts.Models;
using Mone.Contracts.Plugins;
using Mone.Infrastructure.Data;
using Mone.Infrastructure.Data.Entities;
using Mone.Infrastructure.Services;

namespace Mone.Api.Services;

public sealed class ProbeAssignmentMetricMaterializer
{
    private readonly MoneDbContext _db;
    private readonly Mone.PluginEngine.PluginEngine _pluginEngine;
    private readonly ILogger<ProbeAssignmentMetricMaterializer> _logger;

    public ProbeAssignmentMetricMaterializer(
        MoneDbContext db,
        Mone.PluginEngine.PluginEngine pluginEngine,
        ILogger<ProbeAssignmentMetricMaterializer> logger)
    {
        _db = db;
        _pluginEngine = pluginEngine;
        _logger = logger;
    }

    public async Task<int> MaterializeAsync(ProbeAssignmentEntity assignment, CancellationToken ct)
    {
        var registration = _pluginEngine.Registry.Get(assignment.ProbePluginId);
        if (registration is null)
        {
            _logger.LogWarning(
                "Plugin {PluginId} is not loaded; skipping metric materialization for assignment {AssignmentId}",
                assignment.ProbePluginId, assignment.Id);
            await ReplaceAsync(assignment.Id, Array.Empty<ProbeAssignmentMetricEntity>(), ct);
            return 0;
        }

        if (registration.Plugin is not IProbePlugin probe)
        {
            _logger.LogWarning(
                "Plugin {PluginId} is not a probe plugin; skipping metric materialization",
                assignment.ProbePluginId);
            await ReplaceAsync(assignment.Id, Array.Empty<ProbeAssignmentMetricEntity>(), ct);
            return 0;
        }

        var mergedConfig = await ConfigMerger.BuildMergedConfigAsync(
            _db, assignment.ProbePluginId, assignment.ConfigJson, _logger, ct);

        IReadOnlyList<MetricDeclaration> declarations;
        try
        {
            var context = new MaterializationPluginContext(assignment.ProbePluginId, mergedConfig, ct);
            await probe.InitializeAsync(context);
            declarations = await probe.GetMetricsAsync(ct);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex,
                "GetMetricsAsync threw for plugin {PluginId} on assignment {AssignmentId}",
                assignment.ProbePluginId, assignment.Id);
            await ReplaceAsync(assignment.Id, Array.Empty<ProbeAssignmentMetricEntity>(), ct);
            return 0;
        }

        var rows = new List<ProbeAssignmentMetricEntity>(declarations.Count);
        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        foreach (var d in declarations)
        {
            if (string.IsNullOrWhiteSpace(d.Key))
                continue;

            var rawKey = d.Key.Trim();
            var fullKey = rawKey.Contains('.')
                ? rawKey
                : $"{assignment.NameSnakeCase}.{rawKey}";

            if (!seen.Add(fullKey))
                continue;

            rows.Add(new ProbeAssignmentMetricEntity
            {
                Id = Guid.NewGuid(),
                ProbeAssignmentId = assignment.Id,
                RawKey = Truncate(rawKey, 256),
                FullKey = Truncate(fullKey, 384),
                DisplayName = Truncate(string.IsNullOrWhiteSpace(d.DisplayName) ? rawKey : d.DisplayName.Trim(), 256),
                Unit = string.IsNullOrWhiteSpace(d.Unit) ? null : Truncate(d.Unit.Trim(), 64),
                ValueMappingJson = SerializeMapping(d.ValueMapping)
            });
        }

        await ReplaceAsync(assignment.Id, rows, ct);
        return rows.Count;
    }

    // Re-materialize every probe assignment's metric cache. Used at app startup and on
    // housekeeping assess so declaration changes (a plugin dropping/renaming a metric) propagate
    // without requiring each assignment to be manually re-saved. Covers both host- and group-level
    // assignments since they share the ProbeAssignments set.
    public async Task<int> MaterializeAllAsync(CancellationToken ct)
    {
        var assignments = await _db.ProbeAssignments.AsNoTracking().ToListAsync(ct);

        var total = 0;
        foreach (var assignment in assignments)
            total += await MaterializeAsync(assignment, ct);

        _logger.LogInformation(
            "Re-materialized metric declarations for {AssignmentCount} probe assignment(s); {MetricCount} metric row(s) total",
            assignments.Count, total);

        return total;
    }

    private async Task ReplaceAsync(Guid assignmentId, IReadOnlyList<ProbeAssignmentMetricEntity> rows, CancellationToken ct)
    {
        var existing = _db.ProbeAssignmentMetrics.Where(m => m.ProbeAssignmentId == assignmentId);
        _db.ProbeAssignmentMetrics.RemoveRange(existing);
        if (rows.Count > 0)
            _db.ProbeAssignmentMetrics.AddRange(rows);
        await _db.SaveChangesAsync(ct);
    }

    private static string Truncate(string value, int maxLength)
        => value.Length <= maxLength ? value : value[..maxLength];

    private static string? SerializeMapping(IReadOnlyDictionary<double, string>? mapping)
    {
        if (mapping is null || mapping.Count == 0)
            return null;

        var dict = mapping.ToDictionary(kv => kv.Key.ToString(System.Globalization.CultureInfo.InvariantCulture), kv => kv.Value);
        return JsonSerializer.Serialize(dict);
    }

    private sealed class MaterializationPluginContext : IPluginContext
    {
        public MaterializationPluginContext(string pluginId, IReadOnlyDictionary<string, string> configuration, CancellationToken token)
        {
            PluginId = pluginId;
            Configuration = configuration;
            ShutdownToken = token;
        }

        public string PluginId { get; }
        public IReadOnlyDictionary<string, string> Configuration { get; }
        public CancellationToken ShutdownToken { get; }
    }
}
