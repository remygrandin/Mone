using Mone.Contracts.Models;
using Mone.Contracts.Plugins;
using Mone.Contracts.Plugins.Attributes;

namespace MockProbePlugin;

[ProbePlugin(ProbeMode = ProbeMode.Active, InstantiationMode = InstantiationMode.PerTarget)]
public sealed class MockProbe : IProbePlugin
{
    public string Name => "MockProbe";
    public Version Version => new(1, 0, 0);
    public string Description => "Mock probe plugin for integration testing";
    public ProbeMode ProbeMode => ProbeMode.Active;
    public InstantiationMode InstantiationMode => InstantiationMode.PerTarget;

    public Task InitializeAsync(IPluginContext context) => Task.CompletedTask;

    public Task<ProbeResult> ExecuteAsync(string targetId, CancellationToken cancellationToken)
    {
        return Task.FromResult(new ProbeResult(
            MonitoringStatus.Healthy,
            $"Mock probe OK for {targetId}",
            DateTimeOffset.UtcNow,
            TimeSpan.FromMilliseconds(50)));
    }
}
