using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using Mone.CheckerEngine.Services;
using Mone.CheckerEngine.Tests.Fixtures;
using Mone.Contracts.Models;
using Mone.Infrastructure.Data.Entities;
using Mone.Messaging;
using Mone.Messaging.Messages;
using NATS.Client.JetStream;
using NATS.Client.JetStream.Models;
using Xunit;

namespace Mone.CheckerEngine.Tests;

[Collection("CheckerEngine")]
public sealed class CheckerPipelineTests
{
    private readonly CheckerEngineFixture _fixture;

    public CheckerPipelineTests(CheckerEngineFixture fixture)
    {
        _fixture = fixture;
    }

    [Fact]
    public async Task FullPipeline_ProbeResult_ProcessedByChecker_StatusChangePublished_AndPersisted()
    {
        var hostId = Guid.NewGuid();
        var checkerId = "ThresholdChecker";

        await using var db = _fixture.CreateDbContext();
        db.Hosts.Add(new HostEntity { Id = hostId, Name = "test-host", Address = "10.0.0.1" });
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
        var pluginEngine = _fixture.CreatePluginEngineWithChecker();
        var statusTracker = new StatusTracker();
        var scopeFactory = _fixture.CreateScopeFactory();
        var logger = NullLogger<StreamCheckerService>.Instance;

        using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(30));
        var dispatcher = new CheckerDispatcher(
            js, pluginEngine, statusTracker, NullLogger<CheckerDispatcher>.Instance);
        var service = new StreamCheckerService(js, scopeFactory, pluginEngine, dispatcher, logger);

        var statusConsumer = await js.CreateOrUpdateConsumerAsync(
            MoneStreams.StatusChanges.StreamName,
            new ConsumerConfig
            {
                Name = $"test-status-{Guid.NewGuid():N}",
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

        var probeResult = new ProbeResult(
            MonitoringStatus.Unhealthy, "High latency", DateTimeOffset.UtcNow, TimeSpan.FromMilliseconds(500));
        var probeMessage = new ProbeResultMessage(hostId.ToString(), "probe-1", "ping", probeResult);
        await js.PublishAsync($"probes.results.{hostId}", probeMessage, cancellationToken: cts.Token);

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

        await cts.CancelAsync();
        await service.StopAsync(CancellationToken.None);
    }

    [Fact]
    public async Task NoStatusChange_WhenStatusUnchanged_NoEventPublished()
    {
        var hostId = Guid.NewGuid();
        var checkerId = "ThresholdChecker";

        await using var db = _fixture.CreateDbContext();
        db.Hosts.Add(new HostEntity { Id = hostId, Name = "test-host-nochange", Address = "10.0.0.2" });
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
        var pluginEngine = _fixture.CreatePluginEngineWithChecker();
        var statusTracker = new StatusTracker();
        var scopeFactory = _fixture.CreateScopeFactory();
        var logger = NullLogger<StreamCheckerService>.Instance;

        using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(30));
        var dispatcher = new CheckerDispatcher(
            js, pluginEngine, statusTracker, NullLogger<CheckerDispatcher>.Instance);
        var service = new StreamCheckerService(js, scopeFactory, pluginEngine, dispatcher, logger);

        var statusConsumer = await js.CreateOrUpdateConsumerAsync(
            MoneStreams.StatusChanges.StreamName,
            new ConsumerConfig
            {
                Name = $"test-nochange-{Guid.NewGuid():N}",
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

        var probeResult = new ProbeResult(
            MonitoringStatus.Unhealthy, "Down", DateTimeOffset.UtcNow, TimeSpan.FromMilliseconds(100));

        await js.PublishAsync($"probes.results.{hostId}",
            new ProbeResultMessage(hostId.ToString(), "probe-1", "ping", probeResult),
            cancellationToken: cts.Token);
        await Task.Delay(2000, cts.Token);

        await js.PublishAsync($"probes.results.{hostId}",
            new ProbeResultMessage(hostId.ToString(), "probe-1", "ping", probeResult),
            cancellationToken: cts.Token);
        await Task.Delay(2000, cts.Token);

        int messageCount = 0;
        using var countCts = new CancellationTokenSource(TimeSpan.FromSeconds(3));
        try
        {
            await foreach (var msg in statusConsumer.ConsumeAsync<StatusChangeMessage>(cancellationToken: countCts.Token))
            {
                messageCount++;
                await msg.AckAsync(cancellationToken: countCts.Token);
            }
        }
        catch (OperationCanceledException) { }

        Assert.Equal(1, messageCount);

        await cts.CancelAsync();
        await service.StopAsync(CancellationToken.None);
    }

    [Fact]
    public async Task StatusTransition_HealthyToUnhealthy_BothEventsPublished()
    {
        var hostId = Guid.NewGuid();
        var checkerId = "ThresholdChecker";

        await using var db = _fixture.CreateDbContext();
        db.Hosts.Add(new HostEntity { Id = hostId, Name = "test-host-transition", Address = "10.0.0.3" });
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
        var pluginEngine = _fixture.CreatePluginEngineWithChecker();
        var statusTracker = new StatusTracker();
        var scopeFactory = _fixture.CreateScopeFactory();
        var logger = NullLogger<StreamCheckerService>.Instance;

        using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(30));
        var dispatcher = new CheckerDispatcher(
            js, pluginEngine, statusTracker, NullLogger<CheckerDispatcher>.Instance);
        var service = new StreamCheckerService(js, scopeFactory, pluginEngine, dispatcher, logger);

        var statusConsumer = await js.CreateOrUpdateConsumerAsync(
            MoneStreams.StatusChanges.StreamName,
            new ConsumerConfig
            {
                Name = $"test-transition-{Guid.NewGuid():N}",
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

        var healthyResult = new ProbeResult(
            MonitoringStatus.Healthy, "OK", DateTimeOffset.UtcNow, TimeSpan.FromMilliseconds(50));
        await js.PublishAsync($"probes.results.{hostId}",
            new ProbeResultMessage(hostId.ToString(), "probe-1", "ping", healthyResult),
            cancellationToken: cts.Token);
        await Task.Delay(2000, cts.Token);

        var unhealthyResult = new ProbeResult(
            MonitoringStatus.Unhealthy, "Timeout", DateTimeOffset.UtcNow, TimeSpan.FromMilliseconds(5000));
        await js.PublishAsync($"probes.results.{hostId}",
            new ProbeResultMessage(hostId.ToString(), "probe-1", "ping", unhealthyResult),
            cancellationToken: cts.Token);
        await Task.Delay(2000, cts.Token);

        var receivedChanges = new List<StatusChangeMessage>();
        using var collectCts = new CancellationTokenSource(TimeSpan.FromSeconds(5));
        try
        {
            await foreach (var msg in statusConsumer.ConsumeAsync<StatusChangeMessage>(cancellationToken: collectCts.Token))
            {
                receivedChanges.Add(msg.Data!);
                await msg.AckAsync(cancellationToken: collectCts.Token);
                if (receivedChanges.Count >= 2) break;
            }
        }
        catch (OperationCanceledException) { }

        Assert.Equal(2, receivedChanges.Count);

        Assert.Equal(MonitoringStatus.Unknown, receivedChanges[0].Change.PreviousStatus);
        Assert.Equal(MonitoringStatus.Healthy, receivedChanges[0].Change.CurrentStatus);

        Assert.Equal(MonitoringStatus.Healthy, receivedChanges[1].Change.PreviousStatus);
        Assert.Equal(MonitoringStatus.Unhealthy, receivedChanges[1].Change.CurrentStatus);

        await cts.CancelAsync();
        await service.StopAsync(CancellationToken.None);
    }
}
