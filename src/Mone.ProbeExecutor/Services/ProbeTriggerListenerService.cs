using Mone.Messaging;
using Mone.Messaging.Messages;
using Mone.ProbeExecutor.Jobs;
using NATS.Client.JetStream;
using NATS.Client.JetStream.Models;
using Quartz;

namespace Mone.ProbeExecutor.Services;

public sealed class ProbeTriggerListenerService(
    INatsJSContext jetStream,
    ISchedulerFactory schedulerFactory,
    ILogger<ProbeTriggerListenerService> logger) : BackgroundService
{
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        var consumerConfig = new ConsumerConfig
        {
            Name = "probe-trigger-executor",
            DurableName = "probe-trigger-executor",
            FilterSubject = MoneStreams.ProbeTriggers.SubjectPrefix,
            DeliverPolicy = ConsumerConfigDeliverPolicy.New,
            AckPolicy = ConsumerConfigAckPolicy.Explicit
        };

        var consumer = await jetStream.CreateOrUpdateConsumerAsync(
            MoneStreams.ProbeTriggers.StreamName, consumerConfig, stoppingToken);

        logger.LogInformation("Probe trigger listener started, consuming from {Stream}",
            MoneStreams.ProbeTriggers.StreamName);

        await foreach (var msg in consumer.ConsumeAsync<ProbeTriggerMessage>(cancellationToken: stoppingToken))
        {
            try
            {
                var trigger = msg.Data;
                if (trigger is null)
                {
                    await msg.AckAsync(cancellationToken: stoppingToken);
                    continue;
                }

                logger.LogInformation(
                    "Manual trigger received for probe {ProbePluginId} on host {HostId}",
                    trigger.ProbePluginId, trigger.HostId);

                var scheduler = await schedulerFactory.GetScheduler(stoppingToken);

                var job = JobBuilder.Create<ProbeExecutionJob>()
                    .WithIdentity($"manual-{Guid.NewGuid()}", "manual-triggers")
                    .UsingJobData("ProbePluginId", trigger.ProbePluginId)
                    .UsingJobData("TargetId", trigger.HostId.ToString())
                    .UsingJobData("AssignmentId", trigger.AssignmentId.ToString())
                    .StoreDurably(false)
                    .Build();

                if (trigger.TargetAddressOverride is not null)
                    job.JobDataMap.Put("TargetAddressOverride", trigger.TargetAddressOverride);

                if (trigger.HostAddress is not null)
                    job.JobDataMap.Put("HostAddress", trigger.HostAddress);

                var quartzTrigger = TriggerBuilder.Create()
                    .ForJob(job)
                    .StartNow()
                    .Build();

                await scheduler.ScheduleJob(job, quartzTrigger, stoppingToken);

                logger.LogInformation(
                    "Scheduled immediate execution for {ProbePluginId} on host {HostId}",
                    trigger.ProbePluginId, trigger.HostId);

                await msg.AckAsync(cancellationToken: stoppingToken);
            }
            catch (Exception ex)
            {
                logger.LogError(ex, "Failed to process probe trigger message");
                await msg.NakAsync(cancellationToken: stoppingToken);
            }
        }
    }
}
