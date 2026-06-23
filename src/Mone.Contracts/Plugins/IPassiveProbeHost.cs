using Mone.Contracts.Models;

namespace Mone.Contracts.Plugins;

/// <summary>
/// The executor-provided surface a passive probe uses while running. It carries the accumulated
/// per-assignment config (the plugin's ephemeral "what should I listen for" store) and the result
/// sink. The sink spools to local storage when NATS is unreachable, so the plugin emits results
/// without knowing the messaging layer exists.
/// </summary>
public interface IPassiveProbeHost
{
    /// <summary>
    /// Live snapshot of the assignments routed to this plugin. Updated by the executor as
    /// assignments are added or removed; read it per inbound event rather than caching it.
    /// </summary>
    IReadOnlyList<PassiveAssignment> Assignments { get; }

    /// <summary>
    /// Emit a result for a target the plugin resolved from an inbound event. The host builds the
    /// messaging envelope and publishes through the spooling sink.
    /// </summary>
    Task PublishResultAsync(string targetId, ProbeResult result, CancellationToken cancellationToken);

    /// <summary>
    /// Emit a neutral log event for a target the plugin resolved from an inbound event. The host
    /// forwards the raw line verbatim on probes.logs.{targetId} as a best-effort publish (log events
    /// are not spooled). Used by dumb log-emitting probes that forward datagrams without computing a
    /// metric result.
    /// </summary>
    Task PublishLogEventAsync(string targetId, string line, IReadOnlyDictionary<string, object>? metadata, CancellationToken cancellationToken);
}

/// <summary>
/// One resolved passive assignment: the host it represents and the merged config that tells the
/// plugin how to treat traffic for it (e.g. a webhook secret, a per-host filter).
/// </summary>
public sealed record PassiveAssignment(
    Guid HostId,
    string HostAddress,
    Guid AssignmentId,
    IReadOnlyDictionary<string, string> Config);
