using Mone.Contracts.Models;

namespace Mone.Contracts.Plugins;

/// <summary>
/// A passive probe owns its full ingress: it binds its own socket/listener, decodes the wire
/// protocol, authenticates and answers callers, and turns inbound events into <see cref="ProbeResult"/>s.
/// It declares only the transport it needs (<see cref="Protocol"/> + <see cref="Port"/>) so the
/// executor can arbitrate port collisions; everything else is the plugin's responsibility.
///
/// The plugin is a singleton. The executor accumulates one <see cref="PassiveAssignment"/> per
/// matching assignment into the <see cref="IPassiveProbeHost"/> handed to <see cref="StartAsync"/>,
/// and the plugin consults that store to decide which callers/targets it serves. Results are emitted
/// through the host, which spools them when NATS is unreachable — the plugin never learns the
/// transport behind it.
/// </summary>
public interface IPassiveProbePlugin : IProbePlugin
{
    /// <summary>Transport the plugin listens on. The executor ensures no two plugins share a (protocol, port).</summary>
    PassiveProtocol Protocol { get; }

    /// <summary>Port the plugin binds. Constant per plugin so the executor can arbitrate before start-up.</summary>
    int Port { get; }

    /// <summary>
    /// Bind the listener and begin serving. The executor calls this once after winning port
    /// arbitration. The <paramref name="host"/> exposes the live assignment store and the spooling
    /// result sink; both remain valid until <see cref="StopAsync"/> returns.
    /// </summary>
    Task StartAsync(IPassiveProbeHost host, CancellationToken cancellationToken);

    /// <summary>Stop the listener and release the port. Called on shutdown or plugin unload.</summary>
    Task StopAsync(CancellationToken cancellationToken);

    // Passive probes are event-driven; the active-probe surface of IProbePlugin does not apply.
    ProbeMode IProbePlugin.ProbeMode => ProbeMode.Passive;
    InstantiationMode IProbePlugin.InstantiationMode => InstantiationMode.Batch;

    Task<ProbeResult> IProbePlugin.ExecuteAsync(string targetId, CancellationToken cancellationToken)
        => throw new NotSupportedException(
            "Passive probes have no active execution path — they serve their own listener via StartAsync.");
}
