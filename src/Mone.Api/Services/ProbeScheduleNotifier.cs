using Mone.Messaging;
using Mone.Messaging.Messages;
using NATS.Client.JetStream;

namespace Mone.Api.Services;

/// <summary>
/// Publishes a fire-and-forget notification that probe assignments changed, so the
/// ProbeExecutor reconciles its Quartz schedule. Full reconciliation on the consumer
/// side means a dropped publish is self-healed by the periodic fallback reconcile, so
/// a NATS failure here must never fail the originating API request.
/// </summary>
public static class ProbeScheduleNotifier
{
    public static async Task NotifyChangedAsync(
        INatsJSContext jetStream,
        string changeType,
        Guid assignmentId,
        ILogger logger,
        CancellationToken ct)
    {
        try
        {
            var message = new ProbeScheduleChangedMessage(changeType, assignmentId, DateTimeOffset.UtcNow);
            await jetStream.PublishAsync(MoneStreams.ProbeSchedule.ChangedSubject, message, cancellationToken: ct);
            logger.LogInformation("Published probe schedule change ({ChangeType} {AssignmentId})", changeType, assignmentId);
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Failed to publish probe schedule change ({ChangeType} {AssignmentId}); periodic reconcile will recover",
                changeType, assignmentId);
        }
    }
}
