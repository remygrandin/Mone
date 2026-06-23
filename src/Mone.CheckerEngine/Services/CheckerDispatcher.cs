using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Mone.Contracts.Models;
using Mone.Contracts.Plugins;
using Mone.Infrastructure.Data;
using Mone.Infrastructure.Data.Entities;
using Mone.Infrastructure.Services;
using Mone.Messaging.Messages;
using NATS.Client.JetStream;

namespace Mone.CheckerEngine.Services;

public sealed class CheckerDispatcher(
    INatsJSContext jetStream,
    Mone.PluginEngine.PluginEngine pluginEngine,
    StatusTracker statusTracker,
    ILogger<CheckerDispatcher> logger)
{
    public Task DispatchAsync(
        EffectiveCheckerAssignment assignment,
        string targetId,
        string? triggeringProbeId,
        ProbeResult? triggeringResult,
        MoneDbContext db,
        CancellationToken ct)
        => DispatchCoreAsync(assignment, targetId, triggeringProbeId, triggeringResult, logLine: null, db, ct);

    public Task DispatchLogEventAsync(
        EffectiveCheckerAssignment assignment,
        string targetId,
        string? triggeringProbeId,
        string logLine,
        MoneDbContext db,
        CancellationToken ct)
        => DispatchCoreAsync(assignment, targetId, triggeringProbeId, triggeringResult: null, logLine, db, ct);

    private async Task DispatchCoreAsync(
        EffectiveCheckerAssignment assignment,
        string targetId,
        string? triggeringProbeId,
        ProbeResult? triggeringResult,
        string? logLine,
        MoneDbContext db,
        CancellationToken ct)
    {
        var checkerId = assignment.CheckerPluginId;

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

        var pluginContext = new PluginContext(checkerId, config, ct);
        var history = new MetricHistoryAccessor(db);
        var evalContext = new CheckerEvaluationContext(targetId, triggeringProbeId, triggeringResult, history, ct, logLine);

        StatusChange? statusChange;
        try
        {
            await checker.InitializeAsync(pluginContext);
            statusChange = await checker.EvaluateAsync(evalContext);
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Checker {CheckerId} evaluation failed for target {TargetId}", checkerId, targetId);
            return;
        }

        await HandleStatusChangeAsync(checkerId, targetId, statusChange, db, ct);
    }

    private async Task HandleStatusChangeAsync(
        string checkerId,
        string targetId,
        StatusChange? statusChange,
        MoneDbContext db,
        CancellationToken ct)
    {
        if (statusChange is null)
        {
            logger.LogDebug(
                "Checker {CheckerId} skipped target {TargetId} (no opinion on this result)", checkerId, targetId);
            return;
        }

        var (changed, previousStatus) = statusTracker.TryGetStatusChange(
            targetId, checkerId, statusChange.CurrentStatus);

        if (!changed)
        {
            logger.LogDebug("No status change for {CheckerId}/{TargetId}, current={Status}",
                checkerId, targetId, statusChange.CurrentStatus);
            return;
        }

        logger.LogInformation(
            "Status transition for {CheckerId}/{TargetId}: {PreviousStatus} -> {CurrentStatus}",
            checkerId, targetId, previousStatus, statusChange.CurrentStatus);

        var changeWithTrackedPrevious = statusChange with { PreviousStatus = previousStatus };
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
                CurrentStatus = statusChange.CurrentStatus
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
