using System.Collections.Concurrent;
using Mone.Contracts.Models;

namespace Mone.CheckerEngine.Services;

public sealed class StatusTracker
{
    private readonly ConcurrentDictionary<string, MonitoringStatus> _lastKnown = new();

    public MonitoringStatus GetLastKnown(string targetId, string checkerId)
    {
        var key = $"{targetId}:{checkerId}";
        return _lastKnown.GetValueOrDefault(key, MonitoringStatus.Unknown);
    }

    public (bool Changed, MonitoringStatus PreviousStatus) TryGetStatusChange(
        string targetId, string checkerId, MonitoringStatus evaluatedStatus)
    {
        var key = $"{targetId}:{checkerId}";

        while (true)
        {
            if (_lastKnown.TryGetValue(key, out var existing))
            {
                if (existing == evaluatedStatus)
                    return (false, existing);

                if (_lastKnown.TryUpdate(key, evaluatedStatus, existing))
                    return (true, existing);
            }
            else
            {
                if (_lastKnown.TryAdd(key, evaluatedStatus))
                    return (true, MonitoringStatus.Unknown);
            }
        }
    }
}
