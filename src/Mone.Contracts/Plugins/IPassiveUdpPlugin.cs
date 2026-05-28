using Mone.Contracts.Models;

namespace Mone.Contracts.Plugins;

public interface IPassiveUdpPlugin : IProbePlugin
{
    int UdpPort { get; }
    Task<ProbeResult> HandleDatagramAsync(ReadOnlyMemory<byte> datagram, System.Net.IPEndPoint remoteEndpoint, CancellationToken cancellationToken);
}
