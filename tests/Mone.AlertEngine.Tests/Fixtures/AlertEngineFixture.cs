using DotNet.Testcontainers.Builders;
using DotNet.Testcontainers.Containers;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using Mone.Contracts.Plugins;
using Mone.Infrastructure.Data;
using Mone.Messaging;
using Mone.PluginEngine;
using NATS.Client.Core;
using NATS.Client.JetStream;
using NATS.Client.JetStream.Models;
using Testcontainers.PostgreSql;
using Xunit;

namespace Mone.AlertEngine.Tests.Fixtures;

public sealed class AlertEngineFixture : IAsyncLifetime
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

        await Task.WhenAll(_postgres.StartAsync(), _nats.StartAsync());

        _natsConnection = new NatsConnection(new NatsOpts
        {
            Url = NatsUrl,
            SerializerRegistry = NATS.Client.Serializers.Json.NatsJsonSerializerRegistry.Default
        });
        await _natsConnection.ConnectAsync();

        var js = CreateJetStreamContext();
        await js.CreateOrUpdateStreamAsync(
            new StreamConfig(MoneStreams.StatusChanges.StreamName, [MoneStreams.StatusChanges.SubjectPrefix]));

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

    public IServiceScopeFactory CreateScopeFactory()
    {
        var services = new ServiceCollection();
        services.AddDbContext<MoneDbContext>(options =>
            options.UseNpgsql(PostgresConnectionString));
        var provider = services.BuildServiceProvider();
        return provider.GetRequiredService<IServiceScopeFactory>();
    }

    public Mone.PluginEngine.PluginEngine CreatePluginEngineWithNotifier()
    {
        var logger = NullLogger<Mone.PluginEngine.PluginEngine>.Instance;
        var engine = new Mone.PluginEngine.PluginEngine(logger, enableHotReload: false);

        var plugin = new TestNotificationPlugin.ConsoleNotificationPlugin();
        var metadata = new PluginMetadata
        {
            PluginId = plugin.Name,
            Name = plugin.Name,
            Version = plugin.Version,
            Description = plugin.Description,
            PluginTypeName = plugin.GetType().FullName!,
            Kind = PluginKind.Notification,
            AssemblyPath = plugin.GetType().Assembly.Location
        };
        engine.Registry.TryRegister(plugin.Name, new PluginRegistration(plugin, metadata));

        return engine;
    }

    public async ValueTask DisposeAsync()
    {
        await _natsConnection.DisposeAsync();
        await _nats.DisposeAsync();
        await _postgres.DisposeAsync();
    }
}

[CollectionDefinition("AlertEngine")]
public class AlertEngineCollection : ICollectionFixture<AlertEngineFixture>;
