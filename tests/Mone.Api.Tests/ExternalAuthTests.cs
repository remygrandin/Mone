using System.Net;
using System.Net.Http.Json;
using Mone.Api.Models;
using Mone.Api.Services;
using Mone.Api.Tests.Fixtures;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Xunit;

namespace Mone.Api.Tests;

[Collection("Api")]
public class ExternalAuthTests
{
    private readonly ApiFixture _fixture;

    public ExternalAuthTests(ApiFixture fixture) => _fixture = fixture;

    [Fact]
    public async Task Providers_ReturnsEmpty_WhenBothDisabled()
    {
        var client = _fixture.Factory.CreateClient();
        var response = await client.GetAsync("/api/auth/providers");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var providers = await response.Content.ReadFromJsonAsync<List<AuthProviderResponse>>();
        Assert.NotNull(providers);
        Assert.Empty(providers);
    }

    [Fact]
    public async Task Providers_ReturnsOidc_WhenOidcEnabled()
    {
        await using var factory = _fixture.Factory.WithWebHostBuilder(builder =>
        {
            builder.UseSetting("Oidc:Enabled", "true");
            builder.UseSetting("Oidc:Authority", "https://login.example.com");
            builder.UseSetting("Oidc:ClientId", "test-client");
            builder.UseSetting("Oidc:ClientSecret", "test-secret");
        });
        using var client = factory.CreateClient(new WebApplicationFactoryClientOptions
        {
            AllowAutoRedirect = false
        });

        var response = await client.GetAsync("/api/auth/providers");
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);

        var providers = await response.Content.ReadFromJsonAsync<List<AuthProviderResponse>>();
        Assert.NotNull(providers);
        Assert.Contains(providers, p => p.Name == "oidc" && p.LoginUrl == "/api/auth/oidc/login");
    }

    [Fact]
    public async Task LdapLogin_Returns401_WhenAuthFails()
    {
        await using var factory = _fixture.Factory.WithWebHostBuilder(builder =>
        {
            builder.UseSetting("Ldap:Enabled", "true");
            builder.UseSetting("Ldap:Host", "localhost");
            builder.UseSetting("Ldap:BaseDn", "dc=test,dc=com");
            builder.ConfigureServices(services =>
            {
                services.RemoveAll<LdapAuthService>();
                services.AddScoped<LdapAuthService>(_ => new StubLdapAuthService(
                    new LdapAuthResult(false, null, null, null)));
            });
        });
        using var client = factory.CreateClient();

        var response = await client.PostAsJsonAsync("/api/auth/ldap/login",
            new LdapLoginRequest("testuser", "wrongpass"));

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    [Fact]
    public async Task LdapLogin_Returns200_AndProvisions_WhenAuthSucceeds()
    {
        var ldapEmail = $"ldap_{Guid.NewGuid():N}@test.com";
        await using var factory = _fixture.Factory.WithWebHostBuilder(builder =>
        {
            builder.UseSetting("Ldap:Enabled", "true");
            builder.UseSetting("Ldap:Host", "localhost");
            builder.UseSetting("Ldap:BaseDn", "dc=test,dc=com");
            builder.ConfigureServices(services =>
            {
                services.RemoveAll<LdapAuthService>();
                services.AddScoped<LdapAuthService>(_ => new StubLdapAuthService(
                    new LdapAuthResult(true, ldapEmail, "LDAP User", "cn=testuser,dc=test,dc=com")));
            });
        });
        using var client = factory.CreateClient();

        var response = await client.PostAsJsonAsync("/api/auth/ldap/login",
            new LdapLoginRequest("testuser", "correctpass"));

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);

        var token = await response.Content.ReadFromJsonAsync<TokenResponse>();
        Assert.NotNull(token);
        Assert.NotEmpty(token.Token);
        Assert.True(token.Expires > DateTime.UtcNow);
    }

    [Fact]
    public async Task ExternalUser_CannotLogin_ViaLocalAuth()
    {
        var ldapEmail = $"ldap_nopw_{Guid.NewGuid():N}@test.com";
        await using var factory = _fixture.Factory.WithWebHostBuilder(builder =>
        {
            builder.UseSetting("Ldap:Enabled", "true");
            builder.UseSetting("Ldap:Host", "localhost");
            builder.UseSetting("Ldap:BaseDn", "dc=test,dc=com");
            builder.ConfigureServices(services =>
            {
                services.RemoveAll<LdapAuthService>();
                services.AddScoped<LdapAuthService>(_ => new StubLdapAuthService(
                    new LdapAuthResult(true, ldapEmail, "LDAP User", "cn=nopw,dc=test,dc=com")));
            });
        });
        using var client = factory.CreateClient();

        await client.PostAsJsonAsync("/api/auth/ldap/login",
            new LdapLoginRequest("nopw", "ldappass"));

        var localLoginResponse = await client.PostAsJsonAsync("/api/auth/login",
            new LoginRequest(ldapEmail, "AnyPassword1!"));

        Assert.Equal(HttpStatusCode.Unauthorized, localLoginResponse.StatusCode);
    }

    private sealed class StubLdapAuthService : LdapAuthService
    {
        private readonly LdapAuthResult _result;

        public StubLdapAuthService(LdapAuthResult result)
            : base(new Microsoft.Extensions.Configuration.ConfigurationBuilder().Build(),
                   Microsoft.Extensions.Logging.Abstractions.NullLogger<LdapAuthService>.Instance)
        {
            _result = result;
        }

        public override Task<LdapAuthResult> AuthenticateAsync(string username, string password)
        {
            return Task.FromResult(_result);
        }
    }
}
