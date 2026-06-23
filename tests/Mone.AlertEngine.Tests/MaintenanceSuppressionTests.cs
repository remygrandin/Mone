using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using Mone.AlertEngine.Services;
using Mone.AlertEngine.Tests.Fixtures;
using Mone.Contracts.Models;
using Mone.Infrastructure.Data.Entities;
using Mone.Messaging.Messages;
using Xunit;

namespace Mone.AlertEngine.Tests;

[Collection("AlertEngine")]
public sealed class MaintenanceSuppressionTests
{
    private readonly AlertEngineFixture _fixture;

    public MaintenanceSuppressionTests(AlertEngineFixture fixture)
    {
        _fixture = fixture;
    }

    [Fact]
    public async Task ActiveManualWindow_SuppressesDispatch_NoAudit()
    {
        var targetId = Guid.NewGuid();
        var checkerId = "ThresholdChecker";
        var now = DateTimeOffset.UtcNow;

        await SeedHostAsync(targetId);
        await SeedWindowAsync(new MaintenanceWindowEntity
        {
            Id = Guid.NewGuid(),
            HostId = targetId,
            Kind = MaintenanceWindowKind.Manual,
            StartsAt = now.AddMinutes(-1),
            ExpiresAt = now.AddMinutes(5),
            Enabled = true,
            CreatedAt = now,
            UpdatedAt = now
        });

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

        await PublishStatusChangeAsync(js, checkerId, targetId, cts.Token);

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
    public async Task ExpiredWindow_DispatchResumes_OneAudit()
    {
        var targetId = Guid.NewGuid();
        var checkerId = "ThresholdChecker";
        var now = DateTimeOffset.UtcNow;

        await SeedHostAsync(targetId);
        await SeedWindowAsync(new MaintenanceWindowEntity
        {
            Id = Guid.NewGuid(),
            HostId = targetId,
            Kind = MaintenanceWindowKind.Manual,
            StartsAt = now.AddMinutes(-30),
            ExpiresAt = now.AddMinutes(-5),
            Enabled = true,
            CreatedAt = now,
            UpdatedAt = now
        });

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

        await PublishStatusChangeAsync(js, checkerId, targetId, cts.Token);

        await WaitForAuditRecordsAsync(targetId, 1, cts.Token);

        await using var db = _fixture.CreateDbContext();
        var audits = await db.NotificationAudit
            .Where(a => a.TargetId == targetId)
            .ToListAsync(cts.Token);
        Assert.Single(audits);
        Assert.Equal(DeliveryStatus.Sent, audits[0].DeliveryStatus);

        await cts.CancelAsync();
        await service.StopAsync(CancellationToken.None);
    }

    [Fact]
    public async Task ActiveRecurringCronWindow_SuppressesDispatch_NoAudit()
    {
        var targetId = Guid.NewGuid();
        var checkerId = "ThresholdChecker";
        var now = DateTimeOffset.UtcNow;

        await SeedHostAsync(targetId);
        await SeedWindowAsync(new MaintenanceWindowEntity
        {
            Id = Guid.NewGuid(),
            HostId = targetId,
            Kind = MaintenanceWindowKind.RecurringCron,
            Cron = "* * * * *",
            DurationMinutes = 5,
            Enabled = true,
            CreatedAt = now,
            UpdatedAt = now
        });

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

        await PublishStatusChangeAsync(js, checkerId, targetId, cts.Token);

        await Task.Delay(3000, cts.Token);

        await using var db = _fixture.CreateDbContext();
        var audits = await db.NotificationAudit
            .Where(a => a.TargetId == targetId)
            .ToListAsync(cts.Token);
        Assert.Empty(audits);

        await cts.CancelAsync();
        await service.StopAsync(CancellationToken.None);
    }

    private async Task SeedHostAsync(Guid hostId)
    {
        var now = DateTimeOffset.UtcNow;
        await using var db = _fixture.CreateDbContext();
        db.Hosts.Add(new HostEntity
        {
            Id = hostId,
            Name = $"host-{hostId:N}",
            Address = "10.0.0.1",
            Enabled = true,
            CreatedAt = now,
            UpdatedAt = now
        });
        await db.SaveChangesAsync();
    }

    private async Task SeedWindowAsync(MaintenanceWindowEntity window)
    {
        await using var db = _fixture.CreateDbContext();
        db.MaintenanceWindows.Add(window);
        await db.SaveChangesAsync();
    }

    private static async Task PublishStatusChangeAsync(
        NATS.Client.JetStream.NatsJSContext js, string checkerId, Guid targetId, CancellationToken ct)
    {
        var statusChange = new StatusChange(
            targetId.ToString(),
            MonitoringStatus.Healthy,
            MonitoringStatus.Unhealthy,
            new ProbeResult(MonitoringStatus.Unhealthy, "Down", DateTimeOffset.UtcNow, TimeSpan.FromMilliseconds(100)),
            DateTimeOffset.UtcNow);
        await js.PublishAsync($"status.changes.{checkerId}.{targetId}",
            new StatusChangeMessage(checkerId, statusChange), cancellationToken: ct);
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
