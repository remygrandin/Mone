using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using Mone.AlertEngine.Services;
using Mone.AlertEngine.Tests.Fixtures;
using Mone.Contracts.Models;
using Mone.Contracts.Plugins;
using Mone.Infrastructure.Data.Entities;
using Mone.Messaging.Messages;
using Mone.PluginEngine;
using Xunit;

namespace Mone.AlertEngine.Tests;

[Collection("AlertEngine")]
public sealed class AlertDispatcherTests
{
    private readonly AlertEngineFixture _fixture;

    public AlertDispatcherTests(AlertEngineFixture fixture)
    {
        _fixture = fixture;
    }

    [Fact]
    public async Task FullPipeline_StatusChange_DispatchesNotification_PersistsAudit()
    {
        var targetId = Guid.NewGuid();
        var checkerId = "ThresholdChecker";
        var js = _fixture.CreateJetStreamContext();
        var pluginEngine = _fixture.CreatePluginEngineWithNotifier();
        var scopeFactory = _fixture.CreateScopeFactory();
        var logger = NullLogger<AlertDispatcherService>.Instance;
        var consumerName = $"alert-test-{Guid.NewGuid():N}";

        using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(30));
        var service = new AlertDispatcherService(js, scopeFactory, pluginEngine, logger, consumerName);

        _ = Task.Run(async () =>
        {
            try { await service.StartAsync(cts.Token); }
            catch (OperationCanceledException) { }
        }, cts.Token);
        await Task.Delay(1500, cts.Token);

        var statusChange = new StatusChange(
            targetId.ToString(),
            MonitoringStatus.Healthy,
            MonitoringStatus.Unhealthy,
            new ProbeResult(MonitoringStatus.Unhealthy, "High latency", DateTimeOffset.UtcNow, TimeSpan.FromMilliseconds(500)),
            DateTimeOffset.UtcNow);
        await js.PublishAsync($"status.changes.{checkerId}.{targetId}",
            new StatusChangeMessage(checkerId, statusChange), cancellationToken: cts.Token);

        await WaitForAuditRecordsAsync(targetId, 1, cts.Token);

        await using var db = _fixture.CreateDbContext();
        var audits = await db.NotificationAudit
            .Where(a => a.TargetId == targetId)
            .ToListAsync(cts.Token);

        Assert.Single(audits);
        var audit = audits[0];
        Assert.Equal(DeliveryStatus.Sent, audit.DeliveryStatus);
        Assert.Equal(targetId, audit.TargetId);
        Assert.Equal(checkerId, audit.CheckerId);
        Assert.Equal("ConsoleNotifier", audit.NotificationPluginId);
        Assert.Equal(MonitoringStatus.Healthy, audit.PreviousMonitoringStatus);
        Assert.Equal(MonitoringStatus.Unhealthy, audit.CurrentMonitoringStatus);
        Assert.Null(audit.ErrorMessage);

        await cts.CancelAsync();
        await service.StopAsync(CancellationToken.None);
    }

    [Fact]
    public async Task MultiplePlugins_AllInvoked_AllAudited()
    {
        var targetId = Guid.NewGuid();
        var checkerId = "ThresholdChecker";
        var js = _fixture.CreateJetStreamContext();

        var pluginEngine = _fixture.CreatePluginEngineWithNotifier();
        var secondPlugin = new SecondNotificationPlugin();
        var metadata = new PluginMetadata
        {
            PluginId = secondPlugin.Name,
            Name = secondPlugin.Name,
            Version = secondPlugin.Version,
            Description = secondPlugin.Description,
            PluginTypeName = secondPlugin.GetType().FullName!,
            Kind = PluginKind.Notification,
            AssemblyPath = secondPlugin.GetType().Assembly.Location
        };
        pluginEngine.Registry.TryRegister(secondPlugin.Name, new PluginRegistration(secondPlugin, metadata));

        var scopeFactory = _fixture.CreateScopeFactory();
        var logger = NullLogger<AlertDispatcherService>.Instance;
        var consumerName = $"alert-test-{Guid.NewGuid():N}";

        using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(30));
        var service = new AlertDispatcherService(js, scopeFactory, pluginEngine, logger, consumerName);

        _ = Task.Run(async () =>
        {
            try { await service.StartAsync(cts.Token); }
            catch (OperationCanceledException) { }
        }, cts.Token);
        await Task.Delay(1500, cts.Token);

        var statusChange = new StatusChange(
            targetId.ToString(),
            MonitoringStatus.Unknown,
            MonitoringStatus.Unhealthy,
            new ProbeResult(MonitoringStatus.Unhealthy, "Down", DateTimeOffset.UtcNow, TimeSpan.FromMilliseconds(100)),
            DateTimeOffset.UtcNow);
        await js.PublishAsync($"status.changes.{checkerId}.{targetId}",
            new StatusChangeMessage(checkerId, statusChange), cancellationToken: cts.Token);

        await WaitForAuditRecordsAsync(targetId, 2, cts.Token);

        await using var db = _fixture.CreateDbContext();
        var audits = await db.NotificationAudit
            .Where(a => a.TargetId == targetId)
            .OrderBy(a => a.NotificationPluginId)
            .ToListAsync(cts.Token);

        Assert.Equal(2, audits.Count);
        Assert.All(audits, a => Assert.Equal(DeliveryStatus.Sent, a.DeliveryStatus));

        var pluginIds = audits.Select(a => a.NotificationPluginId).OrderBy(x => x).ToList();
        Assert.Contains("ConsoleNotifier", pluginIds);
        Assert.Contains("SecondNotifier", pluginIds);

        await cts.CancelAsync();
        await service.StopAsync(CancellationToken.None);
    }

    [Fact]
    public async Task PluginFailure_ErrorRecorded_OtherPluginsStillInvoked()
    {
        var targetId = Guid.NewGuid();
        var checkerId = "ThresholdChecker";
        var js = _fixture.CreateJetStreamContext();

        var pluginEngine = _fixture.CreatePluginEngineWithNotifier();
        var failingPlugin = new FailingNotificationPlugin();
        var metadata = new PluginMetadata
        {
            PluginId = failingPlugin.Name,
            Name = failingPlugin.Name,
            Version = failingPlugin.Version,
            Description = failingPlugin.Description,
            PluginTypeName = failingPlugin.GetType().FullName!,
            Kind = PluginKind.Notification,
            AssemblyPath = failingPlugin.GetType().Assembly.Location
        };
        pluginEngine.Registry.TryRegister(failingPlugin.Name, new PluginRegistration(failingPlugin, metadata));

        var scopeFactory = _fixture.CreateScopeFactory();
        var logger = NullLogger<AlertDispatcherService>.Instance;
        var consumerName = $"alert-test-{Guid.NewGuid():N}";

        using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(30));
        var service = new AlertDispatcherService(js, scopeFactory, pluginEngine, logger, consumerName);

        _ = Task.Run(async () =>
        {
            try { await service.StartAsync(cts.Token); }
            catch (OperationCanceledException) { }
        }, cts.Token);
        await Task.Delay(1500, cts.Token);

        var statusChange = new StatusChange(
            targetId.ToString(),
            MonitoringStatus.Healthy,
            MonitoringStatus.Unreachable,
            new ProbeResult(MonitoringStatus.Unreachable, "Timeout", DateTimeOffset.UtcNow, TimeSpan.FromMilliseconds(5000)),
            DateTimeOffset.UtcNow);
        await js.PublishAsync($"status.changes.{checkerId}.{targetId}",
            new StatusChangeMessage(checkerId, statusChange), cancellationToken: cts.Token);

        await WaitForAuditRecordsAsync(targetId, 2, cts.Token);

        await using var db = _fixture.CreateDbContext();
        var audits = await db.NotificationAudit
            .Where(a => a.TargetId == targetId)
            .OrderBy(a => a.NotificationPluginId)
            .ToListAsync(cts.Token);

        Assert.Equal(2, audits.Count);

        var consoleAudit = audits.Single(a => a.NotificationPluginId == "ConsoleNotifier");
        Assert.Equal(DeliveryStatus.Sent, consoleAudit.DeliveryStatus);
        Assert.Null(consoleAudit.ErrorMessage);

        var failedAudit = audits.Single(a => a.NotificationPluginId == "FailingNotifier");
        Assert.Equal(DeliveryStatus.Error, failedAudit.DeliveryStatus);
        Assert.Equal("test error", failedAudit.ErrorMessage);

        await cts.CancelAsync();
        await service.StopAsync(CancellationToken.None);
    }

    [Fact]
    public async Task NoPlugins_MessageAcked_NoAudit()
    {
        var targetId = Guid.NewGuid();
        var checkerId = "ThresholdChecker";
        var js = _fixture.CreateJetStreamContext();

        var emptyEngine = new Mone.PluginEngine.PluginEngine(
            NullLogger<Mone.PluginEngine.PluginEngine>.Instance, enableHotReload: false);

        var scopeFactory = _fixture.CreateScopeFactory();
        var logger = NullLogger<AlertDispatcherService>.Instance;
        var consumerName = $"alert-test-{Guid.NewGuid():N}";

        using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(30));
        var service = new AlertDispatcherService(js, scopeFactory, emptyEngine, logger, consumerName);

        _ = Task.Run(async () =>
        {
            try { await service.StartAsync(cts.Token); }
            catch (OperationCanceledException) { }
        }, cts.Token);
        await Task.Delay(1500, cts.Token);

        var statusChange = new StatusChange(
            targetId.ToString(),
            MonitoringStatus.Healthy,
            MonitoringStatus.Unhealthy,
            new ProbeResult(MonitoringStatus.Unhealthy, "Down", DateTimeOffset.UtcNow, TimeSpan.FromMilliseconds(100)),
            DateTimeOffset.UtcNow);
        await js.PublishAsync($"status.changes.{checkerId}.{targetId}",
            new StatusChangeMessage(checkerId, statusChange), cancellationToken: cts.Token);

        await Task.Delay(3000, cts.Token);

        await using var db = _fixture.CreateDbContext();
        var audits = await db.NotificationAudit
            .Where(a => a.TargetId == targetId)
            .ToListAsync(cts.Token);

        Assert.Empty(audits);

        await cts.CancelAsync();
        await service.StopAsync(CancellationToken.None);
    }

    [Fact]
    public async Task ConfigFromDb_PassedToPlugin()
    {
        var targetId = Guid.NewGuid();
        var checkerId = "ThresholdChecker";
        var js = _fixture.CreateJetStreamContext();

        var pluginName = $"CfgCapture_{Guid.NewGuid():N}";
        var capturePlugin = new ConfigCapturingPlugin(pluginName);
        var engine = new Mone.PluginEngine.PluginEngine(
            NullLogger<Mone.PluginEngine.PluginEngine>.Instance, enableHotReload: false);
        var metadata = new PluginMetadata
        {
            PluginId = capturePlugin.Name,
            Name = capturePlugin.Name,
            Version = capturePlugin.Version,
            Description = capturePlugin.Description,
            PluginTypeName = capturePlugin.GetType().FullName!,
            Kind = PluginKind.Notification,
            AssemblyPath = capturePlugin.GetType().Assembly.Location
        };
        engine.Registry.TryRegister(capturePlugin.Name, new PluginRegistration(capturePlugin, metadata));

        await using (var db = _fixture.CreateDbContext())
        {
            db.NotificationConfigs.Add(new NotificationConfigEntity
            {
                Id = Guid.NewGuid(),
                PluginId = pluginName,
                ConfigJson = "{\"api_key\":\"test123\",\"channel\":\"#alerts\"}",
                Enabled = true,
                Scope = "global"
            });
            await db.SaveChangesAsync();
        }

        var scopeFactory = _fixture.CreateScopeFactory();
        var logger = NullLogger<AlertDispatcherService>.Instance;
        var consumerName = $"alert-test-{Guid.NewGuid():N}";

        using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(30));
        var service = new AlertDispatcherService(js, scopeFactory, engine, logger, consumerName);

        _ = Task.Run(async () =>
        {
            try { await service.StartAsync(cts.Token); }
            catch (OperationCanceledException) { }
        }, cts.Token);
        await Task.Delay(1500, cts.Token);

        var statusChange = new StatusChange(
            targetId.ToString(),
            MonitoringStatus.Healthy,
            MonitoringStatus.Unhealthy,
            new ProbeResult(MonitoringStatus.Unhealthy, "Down", DateTimeOffset.UtcNow, TimeSpan.FromMilliseconds(100)),
            DateTimeOffset.UtcNow);
        await js.PublishAsync($"status.changes.{checkerId}.{targetId}",
            new StatusChangeMessage(checkerId, statusChange), cancellationToken: cts.Token);

        await WaitForAuditRecordsAsync(targetId, 1, cts.Token);

        Assert.True(capturePlugin.WasSendCalled);
        Assert.NotNull(capturePlugin.CapturedConfig);
        Assert.Equal("test123", capturePlugin.CapturedConfig["api_key"]);
        Assert.Equal("#alerts", capturePlugin.CapturedConfig["channel"]);

        await cts.CancelAsync();
        await service.StopAsync(CancellationToken.None);
    }

    [Fact]
    public async Task DisabledConfig_PluginSkipped_NoAudit()
    {
        var targetId = Guid.NewGuid();
        var checkerId = "ThresholdChecker";
        var js = _fixture.CreateJetStreamContext();

        var pluginName = $"CfgDisabled_{Guid.NewGuid():N}";
        var capturePlugin = new ConfigCapturingPlugin(pluginName);
        var engine = new Mone.PluginEngine.PluginEngine(
            NullLogger<Mone.PluginEngine.PluginEngine>.Instance, enableHotReload: false);
        var metadata = new PluginMetadata
        {
            PluginId = capturePlugin.Name,
            Name = capturePlugin.Name,
            Version = capturePlugin.Version,
            Description = capturePlugin.Description,
            PluginTypeName = capturePlugin.GetType().FullName!,
            Kind = PluginKind.Notification,
            AssemblyPath = capturePlugin.GetType().Assembly.Location
        };
        engine.Registry.TryRegister(capturePlugin.Name, new PluginRegistration(capturePlugin, metadata));

        await using (var db = _fixture.CreateDbContext())
        {
            db.NotificationConfigs.Add(new NotificationConfigEntity
            {
                Id = Guid.NewGuid(),
                PluginId = pluginName,
                ConfigJson = "{\"key\":\"value\"}",
                Enabled = false,
                Scope = "global"
            });
            await db.SaveChangesAsync();
        }

        var scopeFactory = _fixture.CreateScopeFactory();
        var logger = NullLogger<AlertDispatcherService>.Instance;
        var consumerName = $"alert-test-{Guid.NewGuid():N}";

        using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(30));
        var service = new AlertDispatcherService(js, scopeFactory, engine, logger, consumerName);

        _ = Task.Run(async () =>
        {
            try { await service.StartAsync(cts.Token); }
            catch (OperationCanceledException) { }
        }, cts.Token);
        await Task.Delay(1500, cts.Token);

        var statusChange = new StatusChange(
            targetId.ToString(),
            MonitoringStatus.Healthy,
            MonitoringStatus.Unhealthy,
            new ProbeResult(MonitoringStatus.Unhealthy, "Down", DateTimeOffset.UtcNow, TimeSpan.FromMilliseconds(100)),
            DateTimeOffset.UtcNow);
        await js.PublishAsync($"status.changes.{checkerId}.{targetId}",
            new StatusChangeMessage(checkerId, statusChange), cancellationToken: cts.Token);

        await Task.Delay(3000, cts.Token);

        Assert.False(capturePlugin.WasSendCalled);

        await using var db2 = _fixture.CreateDbContext();
        var audits = await db2.NotificationAudit
            .Where(a => a.TargetId == targetId)
            .ToListAsync(cts.Token);
        Assert.Empty(audits);

        await cts.CancelAsync();
        await service.StopAsync(CancellationToken.None);
    }

    [Fact]
    public async Task NoConfigSeeded_FallsBackToEmptyConfig()
    {
        var targetId = Guid.NewGuid();
        var checkerId = "ThresholdChecker";
        var js = _fixture.CreateJetStreamContext();

        var capturePlugin = new ConfigCapturingPlugin($"CfgEmpty_{Guid.NewGuid():N}");
        var engine = new Mone.PluginEngine.PluginEngine(
            NullLogger<Mone.PluginEngine.PluginEngine>.Instance, enableHotReload: false);
        var metadata = new PluginMetadata
        {
            PluginId = capturePlugin.Name,
            Name = capturePlugin.Name,
            Version = capturePlugin.Version,
            Description = capturePlugin.Description,
            PluginTypeName = capturePlugin.GetType().FullName!,
            Kind = PluginKind.Notification,
            AssemblyPath = capturePlugin.GetType().Assembly.Location
        };
        engine.Registry.TryRegister(capturePlugin.Name, new PluginRegistration(capturePlugin, metadata));

        var scopeFactory = _fixture.CreateScopeFactory();
        var logger = NullLogger<AlertDispatcherService>.Instance;
        var consumerName = $"alert-test-{Guid.NewGuid():N}";

        using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(30));
        var service = new AlertDispatcherService(js, scopeFactory, engine, logger, consumerName);

        _ = Task.Run(async () =>
        {
            try { await service.StartAsync(cts.Token); }
            catch (OperationCanceledException) { }
        }, cts.Token);
        await Task.Delay(1500, cts.Token);

        var statusChange = new StatusChange(
            targetId.ToString(),
            MonitoringStatus.Healthy,
            MonitoringStatus.Unhealthy,
            new ProbeResult(MonitoringStatus.Unhealthy, "Down", DateTimeOffset.UtcNow, TimeSpan.FromMilliseconds(100)),
            DateTimeOffset.UtcNow);
        await js.PublishAsync($"status.changes.{checkerId}.{targetId}",
            new StatusChangeMessage(checkerId, statusChange), cancellationToken: cts.Token);

        await WaitForAuditRecordsAsync(targetId, 1, cts.Token);

        Assert.True(capturePlugin.WasSendCalled);
        Assert.NotNull(capturePlugin.CapturedConfig);
        Assert.Empty(capturePlugin.CapturedConfig);

        await cts.CancelAsync();
        await service.StopAsync(CancellationToken.None);
    }

    private async Task WaitForAuditRecordsAsync(Guid targetId, int expectedCount, CancellationToken ct)
    {
        for (var i = 0; i < 30; i++)
        {
            await Task.Delay(500, ct);
            await using var db = _fixture.CreateDbContext();
            var count = await db.NotificationAudit.CountAsync(a => a.TargetId == targetId, ct);
            if (count >= expectedCount) return;
        }
    }
}

internal sealed class SecondNotificationPlugin : INotificationPlugin
{
    public string Name => "SecondNotifier";
    public Version Version => new(1, 0, 0);
    public string Description => "Second test notification plugin";

    public Task InitializeAsync(IPluginContext context) => Task.CompletedTask;

    public Task<DeliveryResult> SendAsync(StatusChange statusChange, CancellationToken cancellationToken)
        => Task.FromResult(new DeliveryResult(true));
}

internal sealed class FailingNotificationPlugin : INotificationPlugin
{
    public string Name => "FailingNotifier";
    public Version Version => new(1, 0, 0);
    public string Description => "Test plugin that always fails";

    public Task InitializeAsync(IPluginContext context) => Task.CompletedTask;

    public Task<DeliveryResult> SendAsync(StatusChange statusChange, CancellationToken cancellationToken)
        => Task.FromResult(new DeliveryResult(false, "test error"));
}

internal sealed class ConfigCapturingPlugin(string name) : INotificationPlugin
{
    public string Name => name;
    public Version Version => new(1, 0, 0);
    public string Description => "Test plugin that captures received configuration";

    public IReadOnlyDictionary<string, string>? CapturedConfig { get; private set; }
    public bool WasSendCalled { get; private set; }

    public Task InitializeAsync(IPluginContext context)
    {
        CapturedConfig = context.Configuration;
        return Task.CompletedTask;
    }

    public Task<DeliveryResult> SendAsync(StatusChange statusChange, CancellationToken cancellationToken)
    {
        WasSendCalled = true;
        return Task.FromResult(new DeliveryResult(true));
    }
}
