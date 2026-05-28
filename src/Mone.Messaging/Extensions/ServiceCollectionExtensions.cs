using Microsoft.Extensions.DependencyInjection;
using NATS.Client.Core;
using NATS.Client.Hosting;
using NATS.Client.JetStream;
using NATS.Client.Serializers.Json;
using Mone.Messaging.Setup;

namespace Mone.Messaging.Extensions;

public static class ServiceCollectionExtensions
{
    public static IServiceCollection AddMoneMessaging(this IServiceCollection services, string natsUrl)
    {
        services.AddNats(configureOpts: opts => opts with
        {
            Url = natsUrl,
            SerializerRegistry = NatsJsonSerializerRegistry.Default
        });

        services.AddSingleton<INatsJSContext>(sp =>
            new NatsJSContext((NatsConnection)sp.GetRequiredService<INatsConnection>()));

        services.AddHostedService<NatsStreamSetup>();

        return services;
    }
}
