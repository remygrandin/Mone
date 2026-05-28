using Mone.Contracts.Models;
using Mone.Contracts.Plugins;
using Mone.Contracts.Plugins.Attributes;

namespace TestProbePluginV2;

[ProbePlugin(ProbeMode = ProbeMode.Active, InstantiationMode = InstantiationMode.PerTarget)]
public sealed class PingProbePlugin : IProbePlugin
{
    public string Name => "PingProbe";
    public Version Version => new(2, 0, 0);
    public string Description => "V2 test probe plugin that returns Degraded status";
    public ProbeMode ProbeMode => ProbeMode.Active;
    public InstantiationMode InstantiationMode => InstantiationMode.PerTarget;

    public Task InitializeAsync(IPluginContext context) => Task.CompletedTask;

    public Task<ProbeResult> ExecuteAsync(string targetId, CancellationToken cancellationToken)
    {
        return Task.FromResult(new ProbeResult(
            MonitoringStatus.Degraded,
            $"Ping DEGRADED for {targetId} (v2)",
            DateTimeOffset.UtcNow,
            TimeSpan.FromMilliseconds(99)));
    }
}
