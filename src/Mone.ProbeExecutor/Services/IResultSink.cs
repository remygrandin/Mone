using Mone.Messaging.Messages;

namespace Mone.ProbeExecutor.Services;

public interface IResultSink
{
    /// <summary>
    /// Publishes a probe result to NATS. If publishing fails (NATS unreachable), the result is
    /// spooled to local SQLite and forwarded later by <see cref="SpoolForwarderService"/>.
    /// </summary>
    Task PublishAsync(string subject, ProbeResultMessage message, CancellationToken ct);

    /// <summary>
    /// Best-effort publish of a neutral log event. Unlike <see cref="PublishAsync"/>, log events are
    /// NOT spooled on failure — if the publish fails the line is logged and dropped, because the spool
    /// envelope is <see cref="ProbeResultMessage"/>-typed and would mis-deserialize a log payload.
    /// </summary>
    Task PublishLogEventAsync(string subject, ProbeLogEventMessage message, CancellationToken ct);
}
