using System.Net;
using System.Net.Sockets;
using System.Text;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging.Abstractions;
using Mone.CheckerEngine.Services;
using Mone.CheckerEngine.Tests.Fixtures;
using Mone.Contracts.Models;
using Mone.Contracts.Plugins;
using Mone.Infrastructure.Data.Entities;
using Mone.Infrastructure.Services;
using Mone.Messaging;
using Mone.Messaging.Messages;
using Mone.Plugins.Probes.Syslog;
using Mone.ProbeExecutor.Data;
using Mone.ProbeExecutor.Services;
using NATS.Client.JetStream;
using NATS.Client.JetStream.Models;
using Xunit;

namespace Mone.CheckerEngine.Tests;

/// <summary>
/// Keystone end-to-end proof for S03: a real UDP datagram traverses the real SyslogProbePlugin socket,
/// the real PassiveProbeHost/SpoolingResultSink, NATS, and the S02 OnLogEvent checker pipeline to flip a
/// host to Unhealthy — with zero ProbeResult metric rows. S02 proved the consumer half with published
/// messages; this proves the producer half over a live socket.
/// </summary>
[Collection("CheckerEngine")]
public sealed class SyslogLogEventPipelineTests
{
    private readonly CheckerEngineFixture _fixture;

    public SyslogLogEventPipelineTests(CheckerEngineFixture fixture)
    {
        _fixture = fixture;
    }

    [Fact]
    public async Task Syslog_UdpDatagram_ErrorLine_FlipsHostUnhealthy_NoMetricRow()
    {
        await RunUdpPipelineAsync(async (client, endpoint, ct) =>
        {
            var bytes = Encoding.UTF8.GetBytes("<11>May 24 12:00:00 server1 kernel: ERROR disk failure");
            await client.SendAsync(bytes, bytes.Length, endpoint);
        });
    }

    [Fact]
    public async Task Syslog_EmptyDatagram_DroppedListenerSurvives_ThenValidDatagramFlips()
    {
        await RunUdpPipelineAsync(async (client, endpoint, ct) =>
        {
            // An empty frame must be dropped without crashing the receive loop.
            await client.SendAsync(Array.Empty<byte>(), 0, endpoint);
            await Task.Delay(500, ct);
            // The listener survives, so a subsequent valid ERROR frame still flips the host.
            var bytes = Encoding.UTF8.GetBytes("<11>May 24 12:00:00 server1 kernel: ERROR disk failure");
            await client.SendAsync(bytes, bytes.Length, endpoint);
        });
    }

    /// <summary>
    /// Wire the full live pipeline (plugin socket → PassiveProbeHost → SpoolingResultSink → NATS →
    /// StreamCheckerService → RegexChecker → StatusChange/StatusHistory), invoke <paramref name="sendDatagrams"/>
    /// to drive real UDP traffic at the OS-assigned port, then assert the Unhealthy flip and the
    /// zero-metric-row invariant.
    /// </summary>
    private async Task RunUdpPipelineAsync(Func<UdpClient, IPEndPoint, CancellationToken, Task> sendDatagrams)
    {
        var hostId = Guid.NewGuid();
        var checkerId = "LogRegexChecker";

        await using (var db = _fixture.CreateDbContext())
        {
            db.Hosts.Add(new HostEntity { Id = hostId, Name = $"syslog-udp-host-{hostId:N}", Address = "127.0.0.1" });
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
        }

        var js = _fixture.CreateJetStreamContext();
        var pluginEngine = _fixture.CreatePluginEngineWithLogChecker();
        var statusTracker = new StatusTracker();
        var scopeFactory = _fixture.CreateScopeFactory();

        using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(30));

        var dispatcher = new CheckerDispatcher(
            js, pluginEngine, statusTracker, NullLogger<CheckerDispatcher>.Instance);
        var nodeIdentity = new ResolvedNodeIdentity(Guid.NewGuid(), "test-checker", ExecutorRole.Checker);
        var service = new StreamCheckerService(
            js, scopeFactory, pluginEngine, dispatcher, nodeIdentity, NullLogger<StreamCheckerService>.Instance);

        // Real producer half: spooling sink + passive host + plugin owning a real UDP socket.
        var spoolPath = Path.Combine(Path.GetTempPath(), Guid.NewGuid() + ".db");
        var spoolConfig = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?> { ["Mone:Node:SpoolPath"] = spoolPath })
            .Build();
        using var spoolStore = new SpoolStore(spoolConfig, NullLogger<SpoolStore>.Instance);
        var sink = new SpoolingResultSink(js, spoolStore, NullLogger<SpoolingResultSink>.Instance);
        var host = new PassiveProbeHost(sink, "Syslog", "syslog");
        host.UpdateAssignments([
            new PassiveAssignment(hostId, "127.0.0.1", Guid.NewGuid(), new Dictionary<string, string>())
        ]);

        var plugin = new SyslogProbePlugin(0);
        await plugin.StartAsync(host, cts.Token);
        var port = plugin.BoundPort;

        var statusConsumer = await js.CreateOrUpdateConsumerAsync(
            MoneStreams.StatusChanges.StreamName,
            new ConsumerConfig
            {
                Name = $"test-syslog-udp-{Guid.NewGuid():N}",
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

        try
        {
            using var client = new UdpClient();
            var endpoint = new IPEndPoint(IPAddress.Loopback, port);
            await sendDatagrams(client, endpoint, cts.Token);

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
            Assert.Equal(MonitoringStatus.Unhealthy, receivedChange.Change.CurrentStatus);

            await Task.Delay(500, cts.Token);

            await using var verifyDb = _fixture.CreateDbContext();
            var persisted = await verifyDb.StatusHistory
                .Where(sh => sh.TargetId == hostId && sh.CheckerId == checkerId)
                .FirstOrDefaultAsync(cts.Token);

            Assert.NotNull(persisted);
            Assert.Equal(MonitoringStatus.Unhealthy, persisted!.CurrentStatus);

            // Negative invariant: a verbatim log event must NOT persist any metric (ProbeResult) row.
            var metricRowCount = await verifyDb.ProbeResults
                .Where(r => r.TargetId == hostId)
                .CountAsync(cts.Token);
            Assert.Equal(0, metricRowCount);
        }
        finally
        {
            await cts.CancelAsync();
            await plugin.StopAsync(CancellationToken.None);
            await service.StopAsync(CancellationToken.None);
        }
    }
}
