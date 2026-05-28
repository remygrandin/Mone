using Mone.Contracts.Models;
using Mone.Contracts.Plugins;
using Mone.Contracts.Plugins.Attributes;

namespace TestCheckerPlugin;

[CheckerPlugin(CheckerMode = CheckerMode.Stream)]
public sealed class ThresholdCheckerPlugin : ICheckerPlugin
{
    public string Name => "ThresholdChecker";
    public Version Version => new(1, 0, 0);
    public string Description => "Test checker that flags Unhealthy results as status changes";
    public CheckerMode CheckerMode => CheckerMode.Stream;

    public Task InitializeAsync(IPluginContext context) => Task.CompletedTask;

    public Task<StatusChange> EvaluateAsync(string targetId, ProbeResult result, CancellationToken cancellationToken)
    {
        var newStatus = result.Status == MonitoringStatus.Healthy
            ? MonitoringStatus.Healthy
            : MonitoringStatus.Unhealthy;

        return Task.FromResult(new StatusChange(
            targetId,
            MonitoringStatus.Unknown,
            newStatus,
            result,
            DateTimeOffset.UtcNow));
    }
}
