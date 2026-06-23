using Mone.Contracts.Models;
using Mone.Contracts.Plugins;
using Mone.Contracts.Plugins.Attributes;

namespace TestCheckerPlugin;

[CheckerPlugin(InvocationMode = CheckerInvocationMode.OnLogEvent)]
public sealed class LogRegexTestCheckerPlugin : ICheckerPlugin
{
    public string Name => "LogRegexChecker";
    public Version Version => new(1, 0, 0);
    public string Description => "Test OnLogEvent checker mapping ERROR -> Unhealthy, WARN -> Degraded, else Healthy";
    public CheckerInvocationMode InvocationMode => CheckerInvocationMode.OnLogEvent;
    public TimeSpan? Interval => null;

    public Task InitializeAsync(IPluginContext context) => Task.CompletedTask;

    public Task<StatusChange?> EvaluateAsync(CheckerEvaluationContext context)
    {
        if (context.LogLine is not { } line)
            return Task.FromResult<StatusChange?>(null);

        var status = line.Contains("ERROR", StringComparison.Ordinal)
            ? MonitoringStatus.Unhealthy
            : line.Contains("WARN", StringComparison.Ordinal)
                ? MonitoringStatus.Degraded
                : MonitoringStatus.Healthy;

        var syntheticResult = new ProbeResult(status, line, DateTimeOffset.UtcNow, TimeSpan.Zero);

        return Task.FromResult<StatusChange?>(new StatusChange(
            context.TargetId,
            MonitoringStatus.Unknown,
            status,
            syntheticResult,
            DateTimeOffset.UtcNow));
    }
}
