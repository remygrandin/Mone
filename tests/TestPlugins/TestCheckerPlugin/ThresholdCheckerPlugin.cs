using Mone.Contracts.Models;
using Mone.Contracts.Plugins;
using Mone.Contracts.Plugins.Attributes;

namespace TestCheckerPlugin;

[CheckerPlugin(InvocationMode = CheckerInvocationMode.OnProbeResult)]
public sealed class ThresholdCheckerPlugin : ICheckerPlugin
{
    public string Name => "ThresholdChecker";
    public Version Version => new(1, 0, 0);
    public string Description => "Test checker that flags Unhealthy results as status changes";
    public CheckerInvocationMode InvocationMode => CheckerInvocationMode.OnProbeResult;
    public TimeSpan? Interval => null;

    public Task InitializeAsync(IPluginContext context) => Task.CompletedTask;

    public Task<StatusChange> EvaluateAsync(CheckerEvaluationContext context)
    {
        var result = context.TriggeringResult
            ?? throw new InvalidOperationException("TestCheckerPlugin requires a triggering probe result");

        var newStatus = result.Status == MonitoringStatus.Healthy
            ? MonitoringStatus.Healthy
            : MonitoringStatus.Unhealthy;

        return Task.FromResult(new StatusChange(
            context.TargetId,
            MonitoringStatus.Unknown,
            newStatus,
            result,
            DateTimeOffset.UtcNow));
    }
}
