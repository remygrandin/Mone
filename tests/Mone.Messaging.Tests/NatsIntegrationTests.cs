using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using NATS.Client.JetStream;
using NATS.Client.JetStream.Models;
using Mone.Messaging.Extensions;
using Mone.Messaging.Messages;
using Mone.Messaging.Tests.Fixtures;
using Mone.Contracts.Models;
using Xunit;

namespace Mone.Messaging.Tests;

[Trait("Category", "Integration")]
[Collection("NatsContainer")]
public class NatsIntegrationTests : IAsyncLifetime
{
    private readonly NatsContainerFixture _natsFixture;
    private IHost _host = null!;
    private INatsJSContext _js = null!;

    public NatsIntegrationTests(NatsContainerFixture natsFixture) => _natsFixture = natsFixture;

    public async Task InitializeAsync()
    {
        var builder = Host.CreateDefaultBuilder()
            .ConfigureServices(services => services.AddMoneMessaging(_natsFixture.NatsUrl));

        _host = builder.Build();
        await _host.StartAsync();

        _js = _host.Services.GetRequiredService<INatsJSContext>();
    }

    public async Task DisposeAsync()
    {
        await _host.StopAsync();
        _host.Dispose();
    }

    [Fact]
    public async Task ProbeResultMessage_PublishAndConsume_RoundTrips()
    {
        var consumer = await _js.CreateOrUpdateConsumerAsync(
            MoneStreams.ProbeResults.StreamName,
            new ConsumerConfig("test-probe-consumer"));

        var original = new ProbeResultMessage(
            TargetId: "target-1",
            ProbeId: "probe-http-1",
            ProbeType: "HttpProbe",
            Result: new ProbeResult(
                MonitoringStatus.Healthy,
                "OK",
                DateTimeOffset.UtcNow,
                TimeSpan.FromMilliseconds(42)));

        await _js.PublishAsync("probes.results.target-1", original);

        var msg = await consumer.NextAsync<ProbeResultMessage>();
        Assert.NotNull(msg);

        var received = msg!.Data;
        Assert.NotNull(received);
        Assert.Equal(original.TargetId, received!.TargetId);
        Assert.Equal(original.ProbeId, received.ProbeId);
        Assert.Equal(original.ProbeType, received.ProbeType);
        Assert.Equal(original.Result.Status, received.Result.Status);
        Assert.Equal(original.Result.Summary, received.Result.Summary);
    }

    [Fact]
    public async Task StatusChangeMessage_PublishAndConsume_RoundTrips()
    {
        var consumer = await _js.CreateOrUpdateConsumerAsync(
            MoneStreams.StatusChanges.StreamName,
            new ConsumerConfig("test-status-consumer"));

        var original = new StatusChangeMessage(
            CheckerId: "checker-cpu-1",
            Change: new StatusChange(
                "target-1",
                MonitoringStatus.Healthy,
                MonitoringStatus.Degraded,
                new ProbeResult(
                    MonitoringStatus.Degraded,
                    "CPU > 90%",
                    DateTimeOffset.UtcNow,
                    TimeSpan.FromMilliseconds(15)),
                DateTimeOffset.UtcNow));

        await _js.PublishAsync("status.changes.target-1", original);

        var msg = await consumer.NextAsync<StatusChangeMessage>();
        Assert.NotNull(msg);

        var received = msg!.Data;
        Assert.NotNull(received);
        Assert.Equal(original.CheckerId, received!.CheckerId);
        Assert.Equal(original.Change.TargetId, received.Change.TargetId);
        Assert.Equal(original.Change.PreviousStatus, received.Change.PreviousStatus);
        Assert.Equal(original.Change.CurrentStatus, received.Change.CurrentStatus);
    }

    [Fact]
    public async Task NatsStreamSetup_IsIdempotent_CallingTwiceDoesNotError()
    {
        await _host.StopAsync();

        var builder = Host.CreateDefaultBuilder()
            .ConfigureServices(services => services.AddMoneMessaging(_natsFixture.NatsUrl));

        using var host2 = builder.Build();
        await host2.StartAsync();
        await host2.StopAsync();
    }

    [Fact]
    public async Task Streams_ExistAfterSetup()
    {
        var stream = await _js.GetStreamAsync(MoneStreams.ProbeResults.StreamName);
        Assert.Equal(MoneStreams.ProbeResults.StreamName, stream.Info.Config.Name);

        stream = await _js.GetStreamAsync(MoneStreams.StatusChanges.StreamName);
        Assert.Equal(MoneStreams.StatusChanges.StreamName, stream.Info.Config.Name);
    }
}
