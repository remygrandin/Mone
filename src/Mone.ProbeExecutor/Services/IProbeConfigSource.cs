using Mone.Contracts.Models;

namespace Mone.ProbeExecutor.Services;

public interface IProbeConfigSource
{
    /// <summary>
    /// Fetches the resolved probe specs from the console API and refreshes the local cache. If the
    /// API is unreachable, returns the last cached snapshot ("last-known config") instead of failing.
    /// </summary>
    Task<IReadOnlyList<ProbeSpec>> GetProbeSpecsAsync(CancellationToken ct);

    /// <summary>Returns the last cached snapshot without contacting the API (used by manual triggers).</summary>
    Task<IReadOnlyList<ProbeSpec>> GetCachedSpecsAsync(CancellationToken ct);
}
