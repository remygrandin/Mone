using Microsoft.Extensions.Logging.Abstractions;
using Mone.Contracts.Models;
using Mone.Infrastructure.Tests.Fixtures;
using Mone.Messaging;
using Mone.Messaging.Messages;
using Mone.Messaging.Setup;
using NATS.Client.JetStream;
using NATS.Client.JetStream.Models;
using Xunit;

namespace Mone.Infrastructure.Tests;

[Collection("Infrastructure")]
public class NatsMessagingTests(InfrastructureFixture fixture)
{
    [Fact]
    public async Task PublishAndConsume_ProbeResultMessage()
    {
        var js = fixture.CreateJetStreamContext();
        await EnsureStreams(js);

        var targetId = Guid.NewGuid().ToString();
        var message = new ProbeResultMessage(
            targetId,
            "mock-probe",
            "MockProbe",
            new ProbeResult(
                MonitoringStatus.Healthy,
                "Test message",
                DateTimeOffset.UtcNow,
                TimeSpan.FromMilliseconds(25)));

        var subject = $"probes.results.mock-probe.{targetId}";
        await js.PublishAsync(subject, message);

        var consumerName = $"test-consumer-{Guid.NewGuid():N}";
        var consumer = await js.CreateOrUpdateConsumerAsync(
            MoneStreams.ProbeResults.StreamName,
            new ConsumerConfig(consumerName)
            {
                FilterSubject = subject
            });

        var received = await consumer.NextAsync<ProbeResultMessage>();
        Assert.NotNull(received);
        Assert.Equal(targetId, received!.Data!.TargetId);
        Assert.Equal("mock-probe", received.Data!.ProbeId);
        Assert.Equal(MonitoringStatus.Healthy, received.Data!.Result.Status);
    }

    [Fact]
    public async Task StreamSetup_IsIdempotent()
    {
        var setup = new NatsStreamSetup(fixture.NatsConnection, NullLogger<NatsStreamSetup>.Instance);

        await setup.StartAsync(CancellationToken.None);
        await setup.StartAsync(CancellationToken.None);

        var js = fixture.CreateJetStreamContext();
        var stream = await js.GetStreamAsync(MoneStreams.ProbeResults.StreamName);
        Assert.NotNull(stream);
    }

    [Fact]
    public async Task SubjectRouting_MatchesExpectedPattern()
    {
        var js = fixture.CreateJetStreamContext();
        await EnsureStreams(js);

        var subject = "probes.results.mock.target-001";
        var message = new ProbeResultMessage(
            "target-001",
            "mock",
            "MockProbe",
            new ProbeResult(
                MonitoringStatus.Degraded,
                "Routing test",
                DateTimeOffset.UtcNow,
                TimeSpan.FromMilliseconds(5)));

        await js.PublishAsync(subject, message);

        var consumerName = $"routing-test-{Guid.NewGuid():N}";
        var consumer = await js.CreateOrUpdateConsumerAsync(
            MoneStreams.ProbeResults.StreamName,
            new ConsumerConfig(consumerName)
            {
                FilterSubject = subject
            });

        var received = await consumer.NextAsync<ProbeResultMessage>();
        Assert.NotNull(received);
        Assert.Equal("target-001", received!.Data!.TargetId);
        Assert.Equal(MonitoringStatus.Degraded, received.Data!.Result.Status);
    }

    private async Task EnsureStreams(NatsJSContext js)
    {
        var setup = new NatsStreamSetup(fixture.NatsConnection, NullLogger<NatsStreamSetup>.Instance);
        await setup.StartAsync(CancellationToken.None);
    }
}
