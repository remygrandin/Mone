using Mone.Contracts.Models;
using Mone.Contracts.Plugins;
using Mone.Contracts.Plugins.Attributes;

namespace TestProbePlugin;

[ProbePlugin(ProbeMode = ProbeMode.Active, InstantiationMode = InstantiationMode.PerTarget)]
public sealed class PingProbePlugin : IProbePlugin
{
    public string Name => "PingProbe";
    public Version Version => new(1, 0, 0);
    public string Description => "Test probe plugin that returns Healthy status";
    public ProbeMode ProbeMode => ProbeMode.Active;
    public InstantiationMode InstantiationMode => InstantiationMode.PerTarget;

    public Task InitializeAsync(IPluginContext context) => Task.CompletedTask;

    public Task<ProbeResult> ExecuteAsync(string targetId, CancellationToken cancellationToken)
    {
        return Task.FromResult(new ProbeResult(
            MonitoringStatus.Healthy,
            $"Ping OK for {targetId}",
            DateTimeOffset.UtcNow,
            TimeSpan.FromMilliseconds(42)));
    }
}
