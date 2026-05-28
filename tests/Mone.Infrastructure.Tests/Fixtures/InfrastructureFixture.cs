using DotNet.Testcontainers.Builders;
using DotNet.Testcontainers.Containers;
using Microsoft.EntityFrameworkCore;
using Mone.Infrastructure.Data;
using NATS.Client.Core;
using NATS.Client.JetStream;
using Testcontainers.PostgreSql;
using Xunit;

namespace Mone.Infrastructure.Tests.Fixtures;

public sealed class InfrastructureFixture : IAsyncLifetime
{
    private readonly PostgreSqlContainer _postgres = new PostgreSqlBuilder()
        .WithImage("timescale/timescaledb-ha:pg17")
        .WithDatabase("mone_test")
        .WithUsername("mone")
        .WithPassword("mone_test_pass")
        .Build();

    private IContainer _nats = null!;
    private NatsConnection _natsConnection = null!;

    public string PostgresConnectionString => _postgres.GetConnectionString();
    public string NatsUrl => $"nats://localhost:{_nats.GetMappedPublicPort(4222)}";
    public NatsConnection NatsConnection => _natsConnection;

    public async Task InitializeAsync()
    {
        _nats = new ContainerBuilder()
            .WithImage("nats:latest")
            .WithCommand("--jetstream", "--http_port", "8222")
            .WithPortBinding(4222, true)
            .WithPortBinding(8222, true)
            .WithWaitStrategy(Wait.ForUnixContainer()
                .UntilHttpRequestIsSucceeded(r => r.ForPort(8222).ForPath("/healthz")))
            .Build();

        await Task.WhenAll(_postgres.StartAsync(), _nats.StartAsync());

        _natsConnection = new NatsConnection(new NatsOpts
        {
            Url = NatsUrl,
            SerializerRegistry = NATS.Client.Serializers.Json.NatsJsonSerializerRegistry.Default
        });
        await _natsConnection.ConnectAsync();

        await using var dbContext = CreateDbContext();
        await dbContext.Database.MigrateAsync();
    }

    public MoneDbContext CreateDbContext()
    {
        var options = new DbContextOptionsBuilder<MoneDbContext>()
            .UseNpgsql(PostgresConnectionString)
            .Options;
        return new MoneDbContext(options);
    }

    public NatsJSContext CreateJetStreamContext()
    {
        return new NatsJSContext(NatsConnection);
    }

    public async Task DisposeAsync()
    {
        await _natsConnection.DisposeAsync();
        await _nats.DisposeAsync();
        await _postgres.DisposeAsync();
    }
}

[CollectionDefinition("Infrastructure")]
public class InfrastructureCollection : ICollectionFixture<InfrastructureFixture>;
