using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using NATS.Client.Core;
using NATS.Client.JetStream;
using NATS.Client.JetStream.Models;

namespace Mone.Messaging.Setup;

public sealed class NatsStreamSetup(INatsConnection connection, ILogger<NatsStreamSetup> logger) : IHostedService
{
    public async Task StartAsync(CancellationToken cancellationToken)
    {
        var js = new NatsJSContext((NatsConnection)connection);

        await CreateOrUpdateStream(js, MoneStreams.ProbeResults.StreamName,
            [MoneStreams.ProbeResults.SubjectPrefix], cancellationToken);

        await CreateOrUpdateStream(js, MoneStreams.ProbeLogs.StreamName,
            [MoneStreams.ProbeLogs.SubjectPrefix], cancellationToken);

        await CreateOrUpdateStream(js, MoneStreams.StatusChanges.StreamName,
            [MoneStreams.StatusChanges.SubjectPrefix], cancellationToken);

        await CreateOrUpdateStream(js, MoneStreams.ProbeTriggers.StreamName,
            [MoneStreams.ProbeTriggers.SubjectPrefix], cancellationToken);

        await CreateOrUpdateStream(js, MoneStreams.ProbeSchedule.StreamName,
            [MoneStreams.ProbeSchedule.SubjectPrefix], cancellationToken);

        logger.LogInformation("NATS JetStream streams initialized");
    }

    public Task StopAsync(CancellationToken cancellationToken) => Task.CompletedTask;

    private async Task CreateOrUpdateStream(NatsJSContext js, string name, string[] subjects,
        CancellationToken cancellationToken)
    {
        var config = new StreamConfig(name, subjects);
        await js.CreateOrUpdateStreamAsync(config, cancellationToken);
        logger.LogDebug("Stream {StreamName} created or updated", name);
    }
}
