namespace Mone.Contracts.Models;

public interface IMetricHistoryAccessor
{
    Task<IReadOnlyList<ProbeResultRecord>> GetRecentAsync(
        string targetId,
        int count,
        string? probeId = null,
        CancellationToken cancellationToken = default);

    Task<IReadOnlyList<ProbeResultRecord>> GetSinceAsync(
        string targetId,
        DateTimeOffset since,
        string? probeId = null,
        CancellationToken cancellationToken = default);
}
