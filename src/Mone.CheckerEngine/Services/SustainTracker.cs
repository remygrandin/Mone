using System.Collections.Concurrent;
using Mone.Contracts.Models;

namespace Mone.CheckerEngine.Services;

public sealed class SustainTracker
{
    private readonly ConcurrentDictionary<string, PendingChange> _pending = new();

    private sealed record PendingChange(
        MonitoringStatus EvaluatedStatus,
        int ConsecutiveCount,
        DateTimeOffset FirstSeen);

    public MonitoringStatus GetEffectiveStatus(
        string targetId, string checkerId,
        MonitoringStatus evaluatedStatus,
        MonitoringStatus lastKnownStatus,
        int sustainEntries, int sustainMinutes)
    {
        if (sustainEntries <= 0 && sustainMinutes <= 0)
            return evaluatedStatus;

        var key = $"{targetId}:{checkerId}";

        if (evaluatedStatus == lastKnownStatus)
        {
            _pending.TryRemove(key, out _);
            return evaluatedStatus;
        }

        var pending = _pending.AddOrUpdate(key,
            _ => new PendingChange(evaluatedStatus, 1, DateTimeOffset.UtcNow),
            (_, existing) =>
            {
                if (existing.EvaluatedStatus == evaluatedStatus)
                    return existing with { ConsecutiveCount = existing.ConsecutiveCount + 1 };
                return new PendingChange(evaluatedStatus, 1, DateTimeOffset.UtcNow);
            });

        var entriesMet = sustainEntries <= 0 || pending.ConsecutiveCount >= sustainEntries;
        var minutesMet = sustainMinutes <= 0 ||
            (DateTimeOffset.UtcNow - pending.FirstSeen).TotalMinutes >= sustainMinutes;

        if (entriesMet && minutesMet)
        {
            _pending.TryRemove(key, out _);
            return evaluatedStatus;
        }

        return lastKnownStatus;
    }
}
