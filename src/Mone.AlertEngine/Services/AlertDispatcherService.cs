using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Mone.Contracts.Plugins;
using Mone.Infrastructure.Data;
using Mone.Infrastructure.Data.Entities;
using Mone.Infrastructure.Maintenance;
using Mone.Messaging;
using Mone.Messaging.Messages;
using Mone.PluginEngine;
using NATS.Client.JetStream;
using NATS.Client.JetStream.Models;

namespace Mone.AlertEngine.Services;

public class AlertDispatcherService(
    INatsJSContext jetStream,
    IServiceScopeFactory scopeFactory,
    Mone.PluginEngine.PluginEngine pluginEngine,
    ILogger<AlertDispatcherService> logger,
    string consumerName = "alert-engine") : BackgroundService
{
    protected INatsJSContext JetStream { get; } = jetStream;

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        logger.LogInformation("AlertDispatcherService starting, creating durable consumer on {Stream}",
            MoneStreams.StatusChanges.StreamName);

        var consumerConfig = new ConsumerConfig
        {
            Name = consumerName,
            DurableName = consumerName,
            DeliverPolicy = ConsumerConfigDeliverPolicy.New,
            AckPolicy = ConsumerConfigAckPolicy.Explicit,
            FilterSubject = "status.changes.>"
        };

        var consumer = await JetStream.CreateOrUpdateConsumerAsync(
            MoneStreams.StatusChanges.StreamName, consumerConfig, stoppingToken);

        logger.LogInformation("AlertDispatcherService consumer ready, beginning message loop");

        await foreach (var msg in consumer.ConsumeAsync<StatusChangeMessage>(cancellationToken: stoppingToken))
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

    private async Task ProcessMessageAsync(INatsJSMsg<StatusChangeMessage> msg, CancellationToken ct)
    {
        var statusChange = msg.Data!;
        var checkerId = statusChange.CheckerId;
        var change = statusChange.Change;

        logger.LogInformation(
            "Received status change for checker {CheckerId}, target {TargetId}: {PreviousStatus} -> {CurrentStatus}",
            checkerId, change.TargetId, change.PreviousStatus, change.CurrentStatus);

        if (Guid.TryParse(change.TargetId, out var hostId))
        {
            using var maintenanceScope = scopeFactory.CreateScope();
            var maintenanceDb = maintenanceScope.ServiceProvider.GetRequiredService<MoneDbContext>();
            var windows = await maintenanceDb.MaintenanceWindows
                .Where(w => w.HostId == hostId && w.Enabled)
                .ToListAsync(ct);

            if (MaintenanceWindowEvaluator.IsInMaintenance(windows, DateTimeOffset.UtcNow))
            {
                logger.LogInformation(
                    "Status change for checker {CheckerId}, target {TargetId} suppressed by maintenance window, skipping dispatch",
                    checkerId, change.TargetId);
                return;
            }
        }

        var notifications = pluginEngine.GetPluginsByKind(PluginKind.Notification);

        if (notifications.Count == 0)
        {
            logger.LogWarning("No notification plugins loaded, skipping dispatch");
            return;
        }

        logger.LogDebug("Dispatching to {Count} notification plugin(s)", notifications.Count);

        foreach (var registration in notifications)
        {
            await DispatchPluginAsync(registration, checkerId, change, ct);
        }
    }

    private async Task DispatchPluginAsync(
        PluginRegistration registration,
        string checkerId,
        Contracts.Models.StatusChange change,
        CancellationToken ct)
    {
        var pluginId = registration.Metadata.PluginId;

        if (registration.Plugin is not INotificationPlugin plugin)
        {
            logger.LogWarning("Plugin {PluginId} is registered as Notification but does not implement INotificationPlugin",
                pluginId);
            return;
        }

        Dictionary<string, string> configDictionary;
        using (var configScope = scopeFactory.CreateScope())
        {
            var configDb = configScope.ServiceProvider.GetRequiredService<MoneDbContext>();
            var config = await configDb.NotificationConfigs
                .FirstOrDefaultAsync(c => c.PluginId == pluginId, ct);

            logger.LogDebug("Loaded notification config for plugin {PluginId}: {HasConfig}", pluginId, config != null);

            if (config is { Enabled: false })
            {
                logger.LogInformation("Plugin {PluginId} is disabled via notification config, skipping dispatch", pluginId);
                return;
            }

            configDictionary = config?.ConfigJson is not null
                ? JsonSerializer.Deserialize<Dictionary<string, string>>(config.ConfigJson) ?? new Dictionary<string, string>()
                : new Dictionary<string, string>();
        }

        logger.LogDebug("Dispatching notification to plugin {PluginId} for {CheckerId}/{TargetId}",
            pluginId, checkerId, change.TargetId);

        DeliveryStatus deliveryStatus;
        string? errorMessage = null;

        try
        {
            var context = new PluginContext(pluginId, configDictionary, ct);
            await plugin.InitializeAsync(context);
            var result = await plugin.SendAsync(change, ct);

            deliveryStatus = result.Success ? DeliveryStatus.Sent : DeliveryStatus.Error;
            errorMessage = result.ErrorMessage;

            if (result.Success)
            {
                logger.LogInformation("Plugin {PluginId} delivered notification for {CheckerId}/{TargetId}",
                    pluginId, checkerId, change.TargetId);
            }
            else
            {
                logger.LogWarning("Plugin {PluginId} delivery failed for {CheckerId}/{TargetId}: {Error}",
                    pluginId, checkerId, change.TargetId, result.ErrorMessage);
            }
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Plugin {PluginId} threw exception for {CheckerId}/{TargetId}",
                pluginId, checkerId, change.TargetId);
            deliveryStatus = DeliveryStatus.Error;
            errorMessage = ex.Message;
        }

        try
        {
            using var scope = scopeFactory.CreateScope();
            var db = scope.ServiceProvider.GetRequiredService<MoneDbContext>();

            var audit = new NotificationAuditEntity
            {
                Id = Guid.NewGuid(),
                Timestamp = DateTimeOffset.UtcNow,
                TargetId = Guid.Parse(change.TargetId),
                CheckerId = checkerId,
                NotificationPluginId = pluginId,
                DeliveryStatus = deliveryStatus,
                ErrorMessage = errorMessage,
                RetryCount = 0,
                PreviousMonitoringStatus = change.PreviousStatus,
                CurrentMonitoringStatus = change.CurrentStatus
            };

            db.NotificationAudit.Add(audit);
            await db.SaveChangesAsync(ct);

            logger.LogDebug("Persisted notification audit for plugin {PluginId}, target {TargetId}, status {DeliveryStatus}",
                pluginId, change.TargetId, deliveryStatus);
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Failed to persist notification audit for plugin {PluginId}, target {TargetId}",
                pluginId, change.TargetId);
        }
    }
}
