using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using Mone.Api.Models;
using Mone.Api.Tests.Fixtures;
using Xunit;

namespace Mone.Api.Tests;

[Collection("Api")]
public class AuthEndpointTests
{
    private readonly ApiFixture _fixture;

    public AuthEndpointTests(ApiFixture fixture) => _fixture = fixture;

    [Fact]
    public async Task Register_NewUser_Returns201()
    {
        var client = _fixture.Factory.CreateClient();
        var response = await client.PostAsJsonAsync("/api/auth/register",
            new RegisterRequest($"register201_{Guid.NewGuid():N}@test.com", "ValidPass1!"));

        Assert.Equal(HttpStatusCode.Created, response.StatusCode);

        var user = await response.Content.ReadFromJsonAsync<UserResponse>();
        Assert.NotNull(user);
        Assert.NotEmpty(user.Id);
    }

    [Fact]
    public async Task Login_ValidCredentials_ReturnsToken()
    {
        var client = _fixture.Factory.CreateClient();
        var email = $"login_ok_{Guid.NewGuid():N}@test.com";
        const string password = "ValidPass1!";

        await client.PostAsJsonAsync("/api/auth/register", new RegisterRequest(email, password));
        var response = await client.PostAsJsonAsync("/api/auth/login", new LoginRequest(email, password));

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);

        var token = await response.Content.ReadFromJsonAsync<TokenResponse>();
        Assert.NotNull(token);
        Assert.NotEmpty(token.Token);
    }

    [Fact]
    public async Task Login_InvalidCredentials_Returns401()
    {
        var client = _fixture.Factory.CreateClient();
        var response = await client.PostAsJsonAsync("/api/auth/login",
            new LoginRequest("nobody@test.com", "WrongPass1!"));

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    [Fact]
    public async Task Me_WithValidToken_ReturnsUserInfo()
    {
        var email = $"me_ok_{Guid.NewGuid():N}@test.com";
        var client = await _fixture.CreateAuthenticatedClientAsync(email, "ValidPass1!");

        var response = await client.GetAsync("/api/auth/me");
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);

        var user = await response.Content.ReadFromJsonAsync<UserResponse>();
        Assert.NotNull(user);
        Assert.Equal(email, user.Email);
        client.Dispose();
    }

    [Fact]
    public async Task Me_WithoutToken_Returns401()
    {
        var client = _fixture.Factory.CreateClient();
        var response = await client.GetAsync("/api/auth/me");

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }
}
