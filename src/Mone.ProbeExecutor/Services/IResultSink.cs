using Mone.Messaging.Messages;

namespace Mone.ProbeExecutor.Services;

public interface IResultSink
{
    /// <summary>
    /// Publishes a probe result to NATS. If publishing fails (NATS unreachable), the result is
    /// spooled to local SQLite and forwarded later by <see cref="SpoolForwarderService"/>.
    /// </summary>
    Task PublishAsync(string subject, ProbeResultMessage message, CancellationToken ct);
}
