using Mone.Contracts.Models;

namespace Mone.Contracts.Plugins;

public interface INotificationPlugin : IPlugin
{
    Task<DeliveryResult> SendAsync(StatusChange statusChange, CancellationToken cancellationToken);
}
