using Mone.Contracts.Models;
using Mone.Contracts.Plugins;
using Mone.Messaging.Messages;

namespace Mone.ProbeExecutor.Services;

/// <summary>
/// Per-plugin implementation of <see cref="IPassiveProbeHost"/>. Holds the live assignment snapshot
/// the executor maintains for one passive plugin and forwards every result through the spooling
/// <see cref="IResultSink"/>, so passive results survive a NATS/console outage exactly like active
/// ones — without the plugin knowing NATS exists.
/// </summary>
public sealed class PassiveProbeHost(
    IResultSink resultSink,
    string pluginId,
    string probeType) : IPassiveProbeHost
{
    private volatile IReadOnlyList<PassiveAssignment> _assignments = [];

    public IReadOnlyList<PassiveAssignment> Assignments => _assignments;

    public void UpdateAssignments(IReadOnlyList<PassiveAssignment> assignments) => _assignments = assignments;

    public async Task PublishResultAsync(string targetId, ProbeResult result, CancellationToken cancellationToken)
    {
        var subject = $"probes.results.{probeType}.{targetId}";
        var message = new ProbeResultMessage(targetId, pluginId, probeType, result);
        await resultSink.PublishAsync(subject, message, cancellationToken);
    }
}
