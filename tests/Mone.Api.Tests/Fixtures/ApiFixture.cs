using System.Net.Http.Headers;
using System.Net.Http.Json;
using DotNet.Testcontainers.Builders;
using DotNet.Testcontainers.Containers;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Identity;
using Mone.Infrastructure.Data.Entities;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Hosting;
using Mone.Api.Models;
using Mone.Messaging.Setup;
using NATS.Client.Core;
using NATS.Client.Hosting;
using NATS.Client.Serializers.Json;
using Testcontainers.PostgreSql;
using Xunit;

namespace Mone.Api.Tests.Fixtures;

public sealed class ApiFixture : IAsyncLifetime
{
    private readonly PostgreSqlContainer _postgres = new PostgreSqlBuilder()
        .WithImage("timescale/timescaledb-ha:pg17")
        .WithDatabase("mone_api_test")
        .WithUsername("mone")
        .WithPassword("mone_test_pass")
        .Build();

    private IContainer _nats = null!;
    private WebApplicationFactory<Program> _factory = null!;

    public HttpClient Client { get; private set; } = null!;

    private string NatsUrl => $"nats://localhost:{_nats.GetMappedPublicPort(4222)}";

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

        _factory = new WebApplicationFactory<Program>()
            .WithWebHostBuilder(builder =>
            {
                builder.UseSetting("ConnectionStrings:Postgres", _postgres.GetConnectionString());
                builder.UseSetting("ConnectionStrings:Nats", NatsUrl);
                builder.UseSetting("Jwt:Key", "TestSigningKey_AtLeast32CharsLong!!!!");
                builder.UseSetting("Jwt:Issuer", "MoneTestIssuer");
                builder.UseSetting("Jwt:Audience", "MoneTestAudience");

                builder.ConfigureServices(services =>
                {
                    services.RemoveAll<IHostedService>();

                    services.RemoveAll<INatsConnection>();
                    services.AddNats(configureOpts: opts => opts with
                    {
                        Url = NatsUrl,
                        SerializerRegistry = NatsJsonSerializerRegistry.Default
                    });

                    services.AddHostedService<NatsStreamSetup>();
                });
            });

        Client = _factory.CreateClient();

        using var scope = _factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<Mone.Infrastructure.Data.MoneDbContext>();
        await db.Database.EnsureCreatedAsync();
    }

    public async Task<HttpClient> CreateAuthenticatedClientAsync(
        string email = "test@example.com",
        string password = "Test1234!")
    {
        var client = _factory.CreateClient();

        await client.PostAsJsonAsync("/api/auth/register", new RegisterRequest(email, password));

        await GrantSuperAdminAsync(email);

        var loginResponse = await client.PostAsJsonAsync("/api/auth/login", new LoginRequest(email, password));
        var token = await loginResponse.Content.ReadFromJsonAsync<TokenResponse>();

        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", token!.Token);
        return client;
    }

    /// <summary>
    /// Registers and logs in a user WITHOUT granting any permissions, returning an authenticated
    /// client and the user's id so tests can seed precise scoped grants. Use this for authorization
    /// negative tests (the standard <see cref="CreateAuthenticatedClientAsync"/> grants SuperAdmin).
    /// </summary>
    public async Task<(HttpClient Client, string UserId)> CreatePlainClientAsync(
        string email, string password = "Test1234!")
    {
        var client = _factory.CreateClient();
        await client.PostAsJsonAsync("/api/auth/register", new RegisterRequest(email, password));

        var loginResponse = await client.PostAsJsonAsync("/api/auth/login", new LoginRequest(email, password));
        var token = await loginResponse.Content.ReadFromJsonAsync<TokenResponse>();
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", token!.Token);

        using var scope = _factory.Services.CreateScope();
        var userManager = scope.ServiceProvider.GetRequiredService<UserManager<UserEntity>>();
        var user = await userManager.FindByEmailAsync(email);
        return (client, user!.Id);
    }

    public async Task GrantSuperAdminAsync(string email)
    {
        using var scope = _factory.Services.CreateScope();
        var sp = scope.ServiceProvider;
        var db = sp.GetRequiredService<Mone.Infrastructure.Data.MoneDbContext>();
        var userManager = sp.GetRequiredService<UserManager<UserEntity>>();

        var user = await userManager.FindByEmailAsync(email);
        if (user is null) return;

        var roleId = await Mone.Api.Services.RbacSeeder.EnsureSuperAdminRoleAsync(db);
        await Mone.Api.Services.RbacSeeder.EnsureGlobalAssignmentAsync(db, user.Id, roleId);
    }

    public WebApplicationFactory<Program> Factory => _factory;

    public async Task DisposeAsync()
    {
        Client.Dispose();
        await _factory.DisposeAsync();
        await _nats.DisposeAsync();
        await _postgres.DisposeAsync();
    }
}

[CollectionDefinition("Api")]
public class ApiCollection : ICollectionFixture<ApiFixture>;
