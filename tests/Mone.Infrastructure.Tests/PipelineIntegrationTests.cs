using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using Mone.Contracts.Models;
using Mone.Infrastructure.Data.Entities;
using Mone.Infrastructure.Tests.Fixtures;
using Mone.Messaging;
using Mone.Messaging.Messages;
using Mone.Messaging.Setup;
using Mone.PluginEngine;
using NATS.Client.JetStream;
using NATS.Client.JetStream.Models;
using Xunit;

namespace Mone.Infrastructure.Tests;

[Collection("Infrastructure")]
public class PipelineIntegrationTests(InfrastructureFixture fixture)
{
    [Fact]
    public async Task FullPipeline_ProbeExecute_NatsPublish_DbStore_SqlQuery()
    {
        var pluginDllPath = TestPluginBuilder.BuildPlugin("MockProbePlugin");
        var engine = new Mone.PluginEngine.PluginEngine(NullLogger<Mone.PluginEngine.PluginEngine>.Instance);
        engine.LoadPluginAssembly(pluginDllPath);

        var probes = engine.GetPlugins<Mone.Contracts.Plugins.IProbePlugin>().ToList();
        Assert.Single(probes);
        var probe = probes[0];
        Assert.Equal("MockProbe", probe.Name);

        var registration = engine.GetPluginsByKind(PluginKind.Probe).Single();
        var metadata = registration.Metadata;

        var targetId = Guid.NewGuid();
        var result = await probe.ExecuteAsync(targetId.ToString(), CancellationToken.None);
        Assert.Equal(MonitoringStatus.Healthy, result.Status);
        Assert.Contains("Mock probe OK", result.Summary);

        var js = fixture.CreateJetStreamContext();
        var streamSetup = new NatsStreamSetup(fixture.NatsConnection, NullLogger<NatsStreamSetup>.Instance);
        await streamSetup.StartAsync(CancellationToken.None);

        var message = new ProbeResultMessage(
            targetId.ToString(),
            metadata.PluginId,
            metadata.Name,
            result);

        var subject = $"probes.results.{metadata.PluginId}.{targetId}";
        await js.PublishAsync(subject, message);

        var consumerName = $"pipeline-test-{Guid.NewGuid():N}";
        var consumer = await js.CreateOrUpdateConsumerAsync(
            MoneStreams.ProbeResults.StreamName,
            new ConsumerConfig(consumerName) { FilterSubject = subject });

        var received = await consumer.NextAsync<ProbeResultMessage>();
        Assert.NotNull(received);
        var receivedMsg = received!.Data!;
        Assert.Equal(targetId.ToString(), receivedMsg.TargetId);
        Assert.Equal(MonitoringStatus.Healthy, receivedMsg.Result.Status);

        await using var db = fixture.CreateDbContext();
        db.ProbeResults.Add(new ProbeResultEntity
        {
            Timestamp = receivedMsg.Result.Timestamp,
            TargetId = targetId,
            ProbeId = receivedMsg.ProbeId,
            Status = receivedMsg.Result.Status,
            Summary = receivedMsg.Result.Summary,
            DurationMs = receivedMsg.Result.Duration.TotalMilliseconds,
            MetadataJson = null
        });
        await db.SaveChangesAsync();

        var stored = await db.ProbeResults
            .Where(r => r.TargetId == targetId && r.ProbeId == receivedMsg.ProbeId)
            .SingleAsync();

        Assert.Equal(MonitoringStatus.Healthy, stored.Status);
        Assert.Contains("Mock probe OK", stored.Summary);
        Assert.Equal(50.0, stored.DurationMs, precision: 1);

        engine.UnloadPlugin(pluginDllPath);
    }
}
