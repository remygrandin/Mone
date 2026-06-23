using System.Text.Json;
using Mone.Messaging.Messages;
using Mone.ProbeExecutor.Data;
using NATS.Client.JetStream;

namespace Mone.ProbeExecutor.Services;

/// <summary>
/// Publishes probe results to NATS, spilling to the local SQLite spool on any publish failure so no
/// result is lost while NATS or the console is unreachable. Store-and-forward draining happens in
/// <see cref="SpoolForwarderService"/>.
/// </summary>
public sealed class SpoolingResultSink(
    INatsJSContext jetStream,
    SpoolStore spoolStore,
    ILogger<SpoolingResultSink> logger) : IResultSink
{
    public async Task PublishAsync(string subject, ProbeResultMessage message, CancellationToken ct)
    {
        try
        {
            var ack = await jetStream.PublishAsync(subject, message, cancellationToken: ct);
            ack.EnsureSuccess();
            logger.LogDebug("Published probe result to NATS subject {Subject}", subject);
        }
        catch (Exception ex)
        {
            logger.LogWarning(ex, "Publish to {Subject} failed; spooling result locally", subject);
            await spoolStore.EnqueueResultAsync(subject, JsonSerializer.Serialize(message), ct);
        }
    }

    public async Task PublishLogEventAsync(string subject, ProbeLogEventMessage message, CancellationToken ct)
    {
        try
        {
            var ack = await jetStream.PublishAsync(subject, message, cancellationToken: ct);
            ack.EnsureSuccess();
            logger.LogDebug("Published probe log event to NATS subject {Subject}", subject);
        }
        catch (Exception ex)
        {
            logger.LogWarning(ex, "Publish of log event to {Subject} failed; dropping (logs are not spooled)", subject);
        }
    }
}
