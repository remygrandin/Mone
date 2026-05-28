using Mone.Contracts.Models;
using Mone.Contracts.Plugins;
using Mone.Contracts.Plugins.Attributes;

namespace TestNotificationPlugin;

[NotificationPlugin]
public sealed class ConsoleNotificationPlugin : INotificationPlugin
{
    public string Name => "ConsoleNotifier";
    public Version Version => new(1, 0, 0);
    public string Description => "Test notification plugin that writes to console";

    public Task InitializeAsync(IPluginContext context) => Task.CompletedTask;

    public Task<DeliveryResult> SendAsync(StatusChange statusChange, CancellationToken cancellationToken)
    {
        Console.WriteLine($"[ConsoleNotifier] {statusChange.TargetId}: {statusChange.PreviousStatus} -> {statusChange.CurrentStatus}");
        return Task.FromResult(new DeliveryResult(true));
    }
}
