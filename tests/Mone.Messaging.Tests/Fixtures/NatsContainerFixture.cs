using DotNet.Testcontainers.Builders;
using DotNet.Testcontainers.Containers;
using Xunit;

namespace Mone.Messaging.Tests.Fixtures;

public sealed class NatsContainerFixture : IAsyncLifetime
{
    private IContainer _nats = null!;

    public string NatsUrl { get; private set; } = null!;

    public async ValueTask InitializeAsync()
    {
        _nats = new ContainerBuilder()
            .WithImage("nats:latest")
            .WithCommand("--jetstream", "--http_port", "8222")
            .WithPortBinding(4222, true)
            .WithPortBinding(8222, true)
            .WithWaitStrategy(Wait.ForUnixContainer()
                .UntilHttpRequestIsSucceeded(r => r.ForPort(8222).ForPath("/healthz")))
            .Build();

        await _nats.StartAsync();
        NatsUrl = $"nats://localhost:{_nats.GetMappedPublicPort(4222)}";
    }

    public async ValueTask DisposeAsync()
    {
        await _nats.DisposeAsync();
    }
}

[CollectionDefinition("NatsContainer")]
public class NatsContainerCollection : ICollectionFixture<NatsContainerFixture>;
