using Mone.Contracts.Models;

namespace Mone.Contracts.Plugins;

public interface IPassiveProbePlugin : IProbePlugin
{
    string EndpointPath { get; }
    Task<ProbeResult> HandleAsync(string targetId, string payload, CancellationToken cancellationToken);
}
