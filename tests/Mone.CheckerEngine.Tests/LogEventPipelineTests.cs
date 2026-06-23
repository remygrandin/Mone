using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using Mone.CheckerEngine.Services;
using Mone.CheckerEngine.Tests.Fixtures;
using Mone.Contracts.Models;
using Mone.Infrastructure.Data.Entities;
using Mone.Infrastructure.Services;
using Mone.Messaging;
using Mone.Messaging.Messages;
using NATS.Client.JetStream;
using NATS.Client.JetStream.Models;
using Xunit;

namespace Mone.CheckerEngine.Tests;

[Collection("CheckerEngine")]
public sealed class LogEventPipelineTests
{
    private readonly CheckerEngineFixture _fixture;

    public LogEventPipelineTests(CheckerEngineFixture fixture)
    {
        _fixture = fixture;
    }

    [Fact]
    public async Task LogEvent_ErrorLine_RoutedToOptedInChecker_StatusChangePublishedAndPersisted_NoMetricRow()
    {
        var hostId = Guid.NewGuid();
        var checkerId = "LogRegexChecker";

        await using var db = _fixture.CreateDbContext();
        db.Hosts.Add(new HostEntity { Id = hostId, Name = "log-host-error", Address = "10.1.0.1" });
        db.CheckerAssignments.Add(new CheckerAssignmentEntity
        {
            Id = Guid.NewGuid(),
            HostId = hostId,
            CheckerPluginId = checkerId,
            Name = checkerId,
            NameSnakeCase = checkerId,
            Enabled = true
        });
        await db.SaveChangesAsync();

        var js = _fixture.CreateJetStreamContext();
        var pluginEngine = _fixture.CreatePluginEngineWithLogChecker();
        var statusTracker = new StatusTracker();
        var scopeFactory = _fixture.CreateScopeFactory();
        var logger = NullLogger<StreamCheckerService>.Instance;

        using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(30));
        var dispatcher = new CheckerDispatcher(
            js, pluginEngine, statusTracker, NullLogger<CheckerDispatcher>.Instance);
        var nodeIdentity = new ResolvedNodeIdentity(Guid.NewGuid(), "test-checker", ExecutorRole.Checker);
        var service = new StreamCheckerService(js, scopeFactory, pluginEngine, dispatcher, nodeIdentity, logger);

        var statusConsumer = await js.CreateOrUpdateConsumerAsync(
            MoneStreams.StatusChanges.StreamName,
            new ConsumerConfig
            {
                Name = $"test-logevent-{Guid.NewGuid():N}",
                DeliverPolicy = ConsumerConfigDeliverPolicy.New,
                AckPolicy = ConsumerConfigAckPolicy.Explicit,
                FilterSubject = "status.changes.>"
            }, cts.Token);

        _ = Task.Run(async () =>
        {
            try { await service.StartAsync(cts.Token); }
            catch (OperationCanceledException) { }
        }, cts.Token);
        await Task.Delay(1000, cts.Token);

        var logMessage = new ProbeLogEventMessage(
            hostId.ToString(), "syslog-probe", "syslog", "kernel: ERROR disk failure", DateTimeOffset.UtcNow);
        await js.PublishAsync($"probes.logs.{hostId}", logMessage, cancellationToken: cts.Token);

        StatusChangeMessage? receivedChange = null;
        await foreach (var msg in statusConsumer.ConsumeAsync<StatusChangeMessage>(cancellationToken: cts.Token))
        {
            receivedChange = msg.Data;
            await msg.AckAsync(cancellationToken: cts.Token);
            break;
        }

        Assert.NotNull(receivedChange);
        Assert.Equal(checkerId, receivedChange!.CheckerId);
        Assert.Equal(hostId.ToString(), receivedChange.Change.TargetId);
        Assert.Equal(MonitoringStatus.Unknown, receivedChange.Change.PreviousStatus);
        Assert.Equal(MonitoringStatus.Unhealthy, receivedChange.Change.CurrentStatus);

        await Task.Delay(500, cts.Token);

        await using var verifyDb = _fixture.CreateDbContext();
        var persisted = await verifyDb.StatusHistory
            .Where(sh => sh.TargetId == hostId && sh.CheckerId == checkerId)
            .FirstOrDefaultAsync(cts.Token);

        Assert.NotNull(persisted);
        Assert.Equal(MonitoringStatus.Unknown, persisted!.PreviousStatus);
        Assert.Equal(MonitoringStatus.Unhealthy, persisted.CurrentStatus);

        // Negative proof: a log event must NOT auto-persist any metric (ProbeResult) row.
        var metricRowCount = await verifyDb.ProbeResults
            .Where(r => r.TargetId == hostId)
            .CountAsync(cts.Token);
        Assert.Equal(0, metricRowCount);

        await cts.CancelAsync();
        await service.StopAsync(CancellationToken.None);
    }

    [Fact]
    public async Task LogEvent_WarnLine_RoutedToOptedInChecker_DegradedStatusChange_NoMetricRow()
    {
        var hostId = Guid.NewGuid();
        var checkerId = "LogRegexChecker";

        await using var db = _fixture.CreateDbContext();
        db.Hosts.Add(new HostEntity { Id = hostId, Name = "log-host-warn", Address = "10.1.0.2" });
        db.CheckerAssignments.Add(new CheckerAssignmentEntity
        {
            Id = Guid.NewGuid(),
            HostId = hostId,
            CheckerPluginId = checkerId,
            Name = checkerId,
            NameSnakeCase = checkerId,
            Enabled = true
        });
        await db.SaveChangesAsync();

        var js = _fixture.CreateJetStreamContext();
        var pluginEngine = _fixture.CreatePluginEngineWithLogChecker();
        var statusTracker = new StatusTracker();
        var scopeFactory = _fixture.CreateScopeFactory();
        var logger = NullLogger<StreamCheckerService>.Instance;

        using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(30));
        var dispatcher = new CheckerDispatcher(
            js, pluginEngine, statusTracker, NullLogger<CheckerDispatcher>.Instance);
        var nodeIdentity = new ResolvedNodeIdentity(Guid.NewGuid(), "test-checker", ExecutorRole.Checker);
        var service = new StreamCheckerService(js, scopeFactory, pluginEngine, dispatcher, nodeIdentity, logger);

        var statusConsumer = await js.CreateOrUpdateConsumerAsync(
            MoneStreams.StatusChanges.StreamName,
            new ConsumerConfig
            {
                Name = $"test-logevent-warn-{Guid.NewGuid():N}",
                DeliverPolicy = ConsumerConfigDeliverPolicy.New,
                AckPolicy = ConsumerConfigAckPolicy.Explicit,
                FilterSubject = $"status.changes.{checkerId}.{hostId}"
            }, cts.Token);

        _ = Task.Run(async () =>
        {
            try { await service.StartAsync(cts.Token); }
            catch (OperationCanceledException) { }
        }, cts.Token);
        await Task.Delay(1000, cts.Token);

        var logMessage = new ProbeLogEventMessage(
            hostId.ToString(), "syslog-probe", "syslog", "daemon: WARN high memory usage", DateTimeOffset.UtcNow);
        await js.PublishAsync($"probes.logs.{hostId}", logMessage, cancellationToken: cts.Token);

        StatusChangeMessage? receivedChange = null;
        await foreach (var msg in statusConsumer.ConsumeAsync<StatusChangeMessage>(cancellationToken: cts.Token))
        {
            receivedChange = msg.Data;
            await msg.AckAsync(cancellationToken: cts.Token);
            break;
        }

        Assert.NotNull(receivedChange);
        Assert.Equal(MonitoringStatus.Degraded, receivedChange!.Change.CurrentStatus);

        await Task.Delay(500, cts.Token);

        await using var verifyDb = _fixture.CreateDbContext();
        var metricRowCount = await verifyDb.ProbeResults
            .Where(r => r.TargetId == hostId)
            .CountAsync(cts.Token);
        Assert.Equal(0, metricRowCount);

        await cts.CancelAsync();
        await service.StopAsync(CancellationToken.None);
    }
}
