using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Mone.Contracts.Models;
using Mone.Infrastructure.Data;

namespace Mone.CheckerEngine.Services;

public sealed class MetricHistoryAccessor(MoneDbContext db) : IMetricHistoryAccessor
{
    public async Task<IReadOnlyList<ProbeResultRecord>> GetRecentAsync(
        string targetId,
        int count,
        string? probeId = null,
        CancellationToken cancellationToken = default)
    {
        if (count <= 0)
            return Array.Empty<ProbeResultRecord>();

        if (!Guid.TryParse(targetId, out var targetGuid))
            return Array.Empty<ProbeResultRecord>();

        var query = db.ProbeResults.AsNoTracking().Where(r => r.TargetId == targetGuid);
        if (probeId is not null)
            query = query.Where(r => r.ProbeId == probeId);

        var rows = await query
            .OrderByDescending(r => r.Timestamp)
            .Take(count)
            .ToListAsync(cancellationToken);

        return rows.Select(Map).ToList();
    }

    public async Task<IReadOnlyList<ProbeResultRecord>> GetSinceAsync(
        string targetId,
        DateTimeOffset since,
        string? probeId = null,
        CancellationToken cancellationToken = default)
    {
        if (!Guid.TryParse(targetId, out var targetGuid))
            return Array.Empty<ProbeResultRecord>();

        var query = db.ProbeResults.AsNoTracking()
            .Where(r => r.TargetId == targetGuid && r.Timestamp >= since);
        if (probeId is not null)
            query = query.Where(r => r.ProbeId == probeId);

        var rows = await query
            .OrderByDescending(r => r.Timestamp)
            .ToListAsync(cancellationToken);

        return rows.Select(Map).ToList();
    }

    private static ProbeResultRecord Map(Mone.Infrastructure.Data.Entities.ProbeResultEntity entity)
    {
        IReadOnlyDictionary<string, object>? metadata = null;
        if (entity.MetadataJson is not null)
        {
            try
            {
                metadata = JsonSerializer.Deserialize<Dictionary<string, object>>(entity.MetadataJson);
            }
            catch
            {
                metadata = null;
            }
        }

        var result = new ProbeResult(
            entity.Status,
            entity.Summary,
            entity.Timestamp,
            TimeSpan.FromMilliseconds(entity.DurationMs),
            metadata);

        return new ProbeResultRecord(
            entity.Timestamp,
            entity.TargetId.ToString(),
            entity.ProbeId,
            result);
    }
}
