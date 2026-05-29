using Microsoft.Extensions.DependencyInjection;
using Mone.Contracts.Plugins;
using Mone.Contracts.Models;
using Mone.Infrastructure.Data;
using Mone.Infrastructure.Services;
using Mone.Messaging;
using Mone.Messaging.Messages;
using NATS.Client.JetStream;
using NATS.Client.JetStream.Models;

namespace Mone.CheckerEngine.Services;

public sealed class StreamCheckerService(
    INatsJSContext jetStream,
    IServiceScopeFactory scopeFactory,
    Mone.PluginEngine.PluginEngine pluginEngine,
    CheckerDispatcher dispatcher,
    ILogger<StreamCheckerService> logger) : BackgroundService
{
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        logger.LogInformation("StreamCheckerService starting, creating durable consumer on {Stream}",
            MoneStreams.ProbeResults.StreamName);

        var consumerConfig = new ConsumerConfig
        {
            Name = "checker-engine",
            DurableName = "checker-engine",
            DeliverPolicy = ConsumerConfigDeliverPolicy.All,
            AckPolicy = ConsumerConfigAckPolicy.Explicit,
            FilterSubject = "probes.results.>"
        };

        var consumer = await jetStream.CreateOrUpdateConsumerAsync(
            MoneStreams.ProbeResults.StreamName, consumerConfig, stoppingToken);

        logger.LogInformation("StreamCheckerService consumer ready, beginning message loop");

        await foreach (var msg in consumer.ConsumeAsync<ProbeResultMessage>(cancellationToken: stoppingToken))
        {
            if (msg.Data is null)
            {
                await msg.AckAsync(cancellationToken: stoppingToken);
                continue;
            }

            await ProcessMessageAsync(msg, stoppingToken);
            await msg.AckAsync(cancellationToken: stoppingToken);
        }
    }

    private async Task ProcessMessageAsync(INatsJSMsg<ProbeResultMessage> msg, CancellationToken ct)
    {
        var probeResult = msg.Data!;
        var targetId = probeResult.TargetId;

        logger.LogDebug("Processing probe result for target {TargetId}, probe {ProbeId}, status {Status}",
            targetId, probeResult.ProbeId, probeResult.Result.Status);

        using var scope = scopeFactory.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<MoneDbContext>();
        var resolver = scope.ServiceProvider.GetRequiredService<InheritanceResolver>();
        var effectiveAssignments = await resolver.GetEffectiveCheckerAssignmentsAsync(Guid.Parse(targetId));

        var enabledAssignments = effectiveAssignments
            .Where(a => a.Enabled)
            .Where(a => IsOnProbeResult(a.CheckerPluginId))
            .ToList();

        if (enabledAssignments.Count == 0)
        {
            logger.LogDebug("No effective OnProbeResult checker assignments for target {TargetId}", targetId);
            return;
        }

        logger.LogDebug("Dispatching {Count} OnProbeResult checker(s) for target {TargetId}",
            enabledAssignments.Count, targetId);

        foreach (var assignment in enabledAssignments)
        {
            await dispatcher.DispatchAsync(
                assignment,
                targetId,
                probeResult.ProbeId,
                probeResult.Result,
                db,
                ct);
        }
    }

    private bool IsOnProbeResult(string checkerId)
    {
        var registration = pluginEngine.Registry.Get(checkerId);
        if (registration?.Plugin is not ICheckerPlugin checker)
            return false;
        return checker.InvocationMode == CheckerInvocationMode.OnProbeResult;
    }
}
