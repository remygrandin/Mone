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
    ResolvedNodeIdentity nodeIdentity,
    ILogger<StreamCheckerService> logger) : BackgroundService
{
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        await Task.WhenAll(
            ConsumeProbeResultsAsync(stoppingToken),
            ConsumeLogEventsAsync(stoppingToken));
    }

    private async Task ConsumeProbeResultsAsync(CancellationToken stoppingToken)
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

    private async Task ConsumeLogEventsAsync(CancellationToken stoppingToken)
    {
        logger.LogInformation("StreamCheckerService starting log-event consumer on {Stream}",
            MoneStreams.ProbeLogs.StreamName);

        var consumerConfig = new ConsumerConfig
        {
            Name = "checker-engine-logs",
            DurableName = "checker-engine-logs",
            DeliverPolicy = ConsumerConfigDeliverPolicy.All,
            AckPolicy = ConsumerConfigAckPolicy.Explicit,
            FilterSubject = "probes.logs.>"
        };

        var consumer = await jetStream.CreateOrUpdateConsumerAsync(
            MoneStreams.ProbeLogs.StreamName, consumerConfig, stoppingToken);

        logger.LogInformation("StreamCheckerService log-event consumer ready, beginning message loop");

        await foreach (var msg in consumer.ConsumeAsync<ProbeLogEventMessage>(cancellationToken: stoppingToken))
        {
            if (msg.Data is null)
            {
                await msg.AckAsync(cancellationToken: stoppingToken);
                continue;
            }

            await ProcessLogEventAsync(msg, stoppingToken);
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
            .Where(a => a.ExecutorNodeId is null || a.ExecutorNodeId == nodeIdentity.Id)
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
        return checker.InvocationMode.HasFlag(CheckerInvocationMode.OnProbeResult);
    }

    private async Task ProcessLogEventAsync(INatsJSMsg<ProbeLogEventMessage> msg, CancellationToken ct)
    {
        var logEvent = msg.Data!;
        var targetId = logEvent.TargetId;

        logger.LogDebug("Processing log event for target {TargetId}, probe {ProbeId}",
            targetId, logEvent.ProbeId);

        using var scope = scopeFactory.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<MoneDbContext>();
        var resolver = scope.ServiceProvider.GetRequiredService<InheritanceResolver>();
        var effectiveAssignments = await resolver.GetEffectiveCheckerAssignmentsAsync(Guid.Parse(targetId));

        var enabledAssignments = effectiveAssignments
            .Where(a => a.Enabled)
            .Where(a => a.ExecutorNodeId is null || a.ExecutorNodeId == nodeIdentity.Id)
            .Where(a => IsOnLogEvent(a.CheckerPluginId))
            .ToList();

        if (enabledAssignments.Count == 0)
        {
            logger.LogDebug("No effective OnLogEvent checker assignments for target {TargetId}", targetId);
            return;
        }

        logger.LogDebug("Dispatching {Count} OnLogEvent checker(s) for target {TargetId}",
            enabledAssignments.Count, targetId);

        foreach (var assignment in enabledAssignments)
        {
            await dispatcher.DispatchLogEventAsync(
                assignment,
                targetId,
                logEvent.ProbeId,
                logEvent.Line,
                db,
                ct);
        }
    }

    private bool IsOnLogEvent(string checkerId)
    {
        var registration = pluginEngine.Registry.Get(checkerId);
        if (registration?.Plugin is not ICheckerPlugin checker)
            return false;
        return checker.InvocationMode.HasFlag(CheckerInvocationMode.OnLogEvent);
    }
}
