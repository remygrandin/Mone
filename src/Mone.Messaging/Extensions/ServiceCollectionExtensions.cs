using Microsoft.Extensions.DependencyInjection;
using NATS.Client.Core;
using NATS.Client.Hosting;
using NATS.Client.JetStream;
using NATS.Client.Serializers.Json;
using Mone.Messaging.Setup;

namespace Mone.Messaging.Extensions;

public static class ServiceCollectionExtensions
{
    /// <param name="createStreams">
    /// When true (console/API), registers <see cref="NatsStreamSetup"/> which creates the JetStream
    /// streams at startup — this requires NATS to be reachable on boot. Remote executors pass false:
    /// they only publish/consume, so they register the connection but skip stream creation and can
    /// start (and keep running) while NATS is unreachable, reconnecting automatically.
    /// </param>
    public static IServiceCollection AddMoneMessaging(
        this IServiceCollection services, string natsUrl, bool createStreams = true)
    {
        services.AddNats(configureOpts: opts => opts with
        {
            Url = natsUrl,
            SerializerRegistry = NatsJsonSerializerRegistry.Default
        });

        services.AddSingleton<INatsJSContext>(sp =>
            new NatsJSContext((NatsConnection)sp.GetRequiredService<INatsConnection>()));

        if (createStreams)
            services.AddHostedService<NatsStreamSetup>();

        return services;
    }
}
