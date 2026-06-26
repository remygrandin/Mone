using System.Net;
using System.Net.Http.Json;
using Mone.Api.Models;
using Mone.Api.Tests.Fixtures;
using Xunit;

namespace Mone.Api.Tests;

[Collection("Api")]
public class ThemePreferenceEndpointTests
{
    private readonly ApiFixture _fixture;

    public ThemePreferenceEndpointTests(ApiFixture fixture) => _fixture = fixture;

    [Fact]
    public async Task DefaultThemeIsSystem()
    {
        var email = $"theme_default_{Guid.NewGuid():N}@test.com";
        var client = await _fixture.CreateAuthenticatedClientAsync(email, "ValidPass1!", TestContext.Current.CancellationToken);

        var response = await client.GetAsync("/api/auth/me", TestContext.Current.CancellationToken);
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);

        var user = await response.Content.ReadFromJsonAsync<UserResponse>(cancellationToken: TestContext.Current.CancellationToken);
        Assert.NotNull(user);
        Assert.Equal("System", user.ThemePreference);
        client.Dispose();
    }

    [Fact]
    public async Task PutThenGet_RoundTrips()
    {
        var email = $"theme_roundtrip_{Guid.NewGuid():N}@test.com";
        var client = await _fixture.CreateAuthenticatedClientAsync(email, "ValidPass1!", TestContext.Current.CancellationToken);

        var putResponse = await client.PutAsJsonAsync("/api/auth/me/theme", new UpdateThemeRequest("Dark"), cancellationToken: TestContext.Current.CancellationToken);
        Assert.Equal(HttpStatusCode.NoContent, putResponse.StatusCode);

        var getResponse = await client.GetAsync("/api/auth/me", TestContext.Current.CancellationToken);
        Assert.Equal(HttpStatusCode.OK, getResponse.StatusCode);

        var user = await getResponse.Content.ReadFromJsonAsync<UserResponse>(cancellationToken: TestContext.Current.CancellationToken);
        Assert.NotNull(user);
        Assert.Equal("Dark", user.ThemePreference);
        client.Dispose();
    }

    [Fact]
    public async Task InvalidTheme_Returns400()
    {
        var email = $"theme_invalid_{Guid.NewGuid():N}@test.com";
        var client = await _fixture.CreateAuthenticatedClientAsync(email, "ValidPass1!", TestContext.Current.CancellationToken);

        var response = await client.PutAsJsonAsync("/api/auth/me/theme", new UpdateThemeRequest("Neon"), cancellationToken: TestContext.Current.CancellationToken);
        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        Assert.Equal("application/problem+json", response.Content.Headers.ContentType?.MediaType);
        client.Dispose();
    }
}
