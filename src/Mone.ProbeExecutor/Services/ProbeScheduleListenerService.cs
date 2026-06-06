using Mone.Messaging;
using Mone.Messaging.Messages;
using NATS.Client.JetStream;
using NATS.Client.JetStream.Models;

namespace Mone.ProbeExecutor.Services;

/// <summary>
/// Consumes probe schedule change notifications from NATS and drives an immediate
/// reconcile of the Quartz schedule, so API-side assignment mutations take effect
/// without waiting for the periodic fallback reconcile.
/// </summary>
public sealed class ProbeScheduleListenerService(
    INatsJSContext jetStream,
    ProbeSchedulerService scheduler,
    ILogger<ProbeScheduleListenerService> logger) : BackgroundService
{
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        var consumerConfig = new ConsumerConfig
        {
            Name = "probe-schedule-reconciler",
            DurableName = "probe-schedule-reconciler",
            FilterSubject = MoneStreams.ProbeSchedule.SubjectPrefix,
            DeliverPolicy = ConsumerConfigDeliverPolicy.New,
            AckPolicy = ConsumerConfigAckPolicy.Explicit
        };

        var consumer = await jetStream.CreateOrUpdateConsumerAsync(
            MoneStreams.ProbeSchedule.StreamName, consumerConfig, stoppingToken);

        logger.LogInformation("Probe schedule listener started, consuming from {Stream}",
            MoneStreams.ProbeSchedule.StreamName);

        await foreach (var msg in consumer.ConsumeAsync<ProbeScheduleChangedMessage>(cancellationToken: stoppingToken))
        {
            try
            {
                var change = msg.Data;
                if (change is not null)
                {
                    logger.LogInformation(
                        "Probe schedule change received ({ChangeType} {AssignmentId}), reconciling",
                        change.ChangeType, change.AssignmentId);
                }

                await scheduler.ReconcileAsync(stoppingToken);

                await msg.AckAsync(cancellationToken: stoppingToken);
            }
            catch (Exception ex)
            {
                logger.LogError(ex, "Failed to reconcile after probe schedule change");
                await msg.NakAsync(cancellationToken: stoppingToken);
            }
        }
    }
}
