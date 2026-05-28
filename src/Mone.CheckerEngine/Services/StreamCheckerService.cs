using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Mone.Contracts.Models;
using Mone.Contracts.Plugins;
using Mone.Infrastructure.Data;
using Mone.Infrastructure.Data.Entities;
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
    StatusTracker statusTracker,
    SustainTracker sustainTracker,
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

        var enabledAssignments = effectiveAssignments.Where(a => a.Enabled).ToList();

        if (enabledAssignments.Count == 0)
        {
            logger.LogDebug("No effective checker assignments for target {TargetId}", targetId);
            return;
        }

        logger.LogDebug("Found {Count} effective checker assignments for target {TargetId} ({Direct} direct, {Inherited} inherited)",
            enabledAssignments.Count, targetId,
            enabledAssignments.Count(a => a.SourceType == AssignmentSourceType.Direct),
            enabledAssignments.Count(a => a.SourceType == AssignmentSourceType.Inherited));

        foreach (var assignment in enabledAssignments)
        {
            await DispatchCheckerAsync(assignment, targetId, probeResult.Result, db, ct);
        }
    }

    private async Task DispatchCheckerAsync(
        EffectiveCheckerAssignment assignment,
        string targetId,
        ProbeResult probeResult,
        MoneDbContext db,
        CancellationToken ct)
    {
        var checkerId = assignment.CheckerPluginId;

        logger.LogDebug("Dispatching checker {CheckerId} for target {TargetId}", checkerId, targetId);

        var registration = pluginEngine.Registry.Get(checkerId);
        if (registration is null)
        {
            logger.LogWarning("Checker plugin {CheckerId} not found in registry", checkerId);
            return;
        }

        if (registration.Plugin is not ICheckerPlugin checker)
        {
            logger.LogWarning("Plugin {CheckerId} is not an ICheckerPlugin", checkerId);
            return;
        }

        var config = await ConfigMerger.BuildMergedConfigAsync(db, checkerId, assignment.ConfigJson, logger, ct);

        if (assignment.SourceType == AssignmentSourceType.Inherited)
        {
            logger.LogDebug("Checker {CheckerId} for target {TargetId} inherited from group {GroupId}",
                checkerId, targetId, assignment.SourceGroupId);
        }
        var context = new PluginContext(checkerId, config, ct);

        StatusChange statusChange;
        try
        {
            await checker.InitializeAsync(context);
            statusChange = await checker.EvaluateAsync(targetId, probeResult, ct);
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Checker {CheckerId} evaluation failed for target {TargetId}", checkerId, targetId);
            return;
        }

        int sustainEntries = config.TryGetValue("SustainEntries", out var se) && int.TryParse(se, out var seVal) ? seVal : 0;
        int sustainMinutes = config.TryGetValue("SustainMinutes", out var sm) && int.TryParse(sm, out var smVal) ? smVal : 0;

        var lastKnown = statusTracker.GetLastKnown(targetId, checkerId);
        var effectiveStatus = sustainTracker.GetEffectiveStatus(
            targetId, checkerId, statusChange.CurrentStatus, lastKnown, sustainEntries, sustainMinutes);

        if (effectiveStatus != statusChange.CurrentStatus)
        {
            logger.LogDebug("Sustain not met for {CheckerId}/{TargetId}: evaluated={Evaluated}, sustained={Sustained} (entries={Entries}, minutes={Minutes})",
                checkerId, targetId, statusChange.CurrentStatus, effectiveStatus, sustainEntries, sustainMinutes);
        }

        var (changed, previousStatus) = statusTracker.TryGetStatusChange(
            targetId, checkerId, effectiveStatus);

        if (!changed)
        {
            logger.LogDebug("No status change for {CheckerId}/{TargetId}, current={Status}",
                checkerId, targetId, statusChange.CurrentStatus);
            return;
        }

        logger.LogInformation(
            "Status transition for {CheckerId}/{TargetId}: {PreviousStatus} -> {CurrentStatus}",
            checkerId, targetId, previousStatus, effectiveStatus);

        var changeWithTrackedPrevious = statusChange with { PreviousStatus = previousStatus, CurrentStatus = effectiveStatus };
        var changeMessage = new StatusChangeMessage(checkerId, changeWithTrackedPrevious);

        try
        {
            var subject = $"status.changes.{checkerId}.{targetId}";
            await jetStream.PublishAsync(subject, changeMessage, cancellationToken: ct);
            logger.LogDebug("Published status change to NATS subject {Subject}", subject);
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Failed to publish status change to NATS for {CheckerId}/{TargetId}",
                checkerId, targetId);
        }

        try
        {
            var entity = new StatusHistoryEntity
            {
                Timestamp = statusChange.ChangedAt,
                TargetId = Guid.Parse(targetId),
                CheckerId = checkerId,
                PreviousStatus = previousStatus,
                CurrentStatus = effectiveStatus
            };
            db.StatusHistory.Add(entity);
            await db.SaveChangesAsync(ct);
            logger.LogDebug("Persisted status change to database for {CheckerId}/{TargetId}", checkerId, targetId);
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Failed to persist status change to database for {CheckerId}/{TargetId}",
                checkerId, targetId);
        }
    }

}
