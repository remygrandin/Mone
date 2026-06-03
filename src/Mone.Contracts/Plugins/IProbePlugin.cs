using Mone.Contracts.Models;

namespace Mone.Contracts.Plugins;

public interface IProbePlugin : IPlugin
{
    ProbeMode ProbeMode { get; }
    InstantiationMode InstantiationMode { get; }
    Task<ProbeResult> ExecuteAsync(string targetId, CancellationToken cancellationToken);

    Task<IReadOnlyList<MetricDeclaration>> GetMetricsAsync(CancellationToken cancellationToken)
        => Task.FromResult<IReadOnlyList<MetricDeclaration>>(Array.Empty<MetricDeclaration>());
}
