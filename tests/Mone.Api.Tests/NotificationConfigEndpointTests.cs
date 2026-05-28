using System.Net;
using System.Net.Http.Json;
using Mone.Api.Models;
using Mone.Api.Tests.Fixtures;
using Xunit;

namespace Mone.Api.Tests;

[Collection("Api")]
public class NotificationConfigEndpointTests
{
    private readonly ApiFixture _fixture;

    public NotificationConfigEndpointTests(ApiFixture fixture) => _fixture = fixture;

    [Fact]
    public async Task CreateConfig_Returns201_WithValidResponse()
    {
        using var client = await _fixture.CreateAuthenticatedClientAsync(
            $"notif_create_{Guid.NewGuid():N}@test.com", "ValidPass1!");

        var request = new CreateNotificationConfigRequest(
            "SlackNotification", "{\"webhook_url\":\"https://hooks.slack.com/test\"}", true, "global");

        var response = await client.PostAsJsonAsync("/api/notifications/configs", request);

        Assert.Equal(HttpStatusCode.Created, response.StatusCode);

        var config = await response.Content.ReadFromJsonAsync<NotificationConfigResponse>();
        Assert.NotNull(config);
        Assert.NotEqual(Guid.Empty, config.Id);
        Assert.Equal("SlackNotification", config.PluginId);
        Assert.Equal("{\"webhook_url\":\"https://hooks.slack.com/test\"}", config.ConfigJson);
        Assert.True(config.Enabled);
        Assert.Equal("global", config.Scope);
    }

    [Fact]
    public async Task GetConfigs_ReturnsCreatedConfigs()
    {
        using var client = await _fixture.CreateAuthenticatedClientAsync(
            $"notif_list_{Guid.NewGuid():N}@test.com", "ValidPass1!");

        var request = new CreateNotificationConfigRequest("TeamsNotification", null, true, null);
        await client.PostAsJsonAsync("/api/notifications/configs", request);

        var response = await client.GetAsync("/api/notifications/configs");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);

        var configs = await response.Content.ReadFromJsonAsync<NotificationConfigResponse[]>();
        Assert.NotNull(configs);
        Assert.Contains(configs, c => c.PluginId == "TeamsNotification");
    }

    [Fact]
    public async Task GetConfigById_ReturnsConfig()
    {
        using var client = await _fixture.CreateAuthenticatedClientAsync(
            $"notif_getid_{Guid.NewGuid():N}@test.com", "ValidPass1!");

        var createResp = await client.PostAsJsonAsync("/api/notifications/configs",
            new CreateNotificationConfigRequest("WebhookNotification", "{\"url\":\"https://example.com\"}", true, "host:abc"));
        var created = await createResp.Content.ReadFromJsonAsync<NotificationConfigResponse>();

        var response = await client.GetAsync($"/api/notifications/configs/{created!.Id}");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var config = await response.Content.ReadFromJsonAsync<NotificationConfigResponse>();
        Assert.Equal(created.Id, config!.Id);
        Assert.Equal("WebhookNotification", config.PluginId);
    }

    [Fact]
    public async Task GetConfigById_NonExistent_Returns404()
    {
        using var client = await _fixture.CreateAuthenticatedClientAsync(
            $"notif_404_{Guid.NewGuid():N}@test.com", "ValidPass1!");

        var response = await client.GetAsync($"/api/notifications/configs/{Guid.NewGuid()}");

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
    }

    [Fact]
    public async Task UpdateConfig_ReturnsUpdatedConfig()
    {
        using var client = await _fixture.CreateAuthenticatedClientAsync(
            $"notif_update_{Guid.NewGuid():N}@test.com", "ValidPass1!");

        var createResp = await client.PostAsJsonAsync("/api/notifications/configs",
            new CreateNotificationConfigRequest("SlackNotification", "{\"channel\":\"#old\"}", true, null));
        var created = await createResp.Content.ReadFromJsonAsync<NotificationConfigResponse>();

        var updateRequest = new UpdateNotificationConfigRequest(
            "SlackNotification", "{\"channel\":\"#new\"}", false, "global");

        var response = await client.PutAsJsonAsync($"/api/notifications/configs/{created!.Id}", updateRequest);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);

        var updated = await response.Content.ReadFromJsonAsync<NotificationConfigResponse>();
        Assert.NotNull(updated);
        Assert.Equal(created.Id, updated.Id);
        Assert.Equal("{\"channel\":\"#new\"}", updated.ConfigJson);
        Assert.False(updated.Enabled);
        Assert.Equal("global", updated.Scope);
    }

    [Fact]
    public async Task DeleteConfig_Returns204()
    {
        using var client = await _fixture.CreateAuthenticatedClientAsync(
            $"notif_delete_{Guid.NewGuid():N}@test.com", "ValidPass1!");

        var createResp = await client.PostAsJsonAsync("/api/notifications/configs",
            new CreateNotificationConfigRequest("TeamsNotification", null, true, null));
        var created = await createResp.Content.ReadFromJsonAsync<NotificationConfigResponse>();

        var deleteResp = await client.DeleteAsync($"/api/notifications/configs/{created!.Id}");

        Assert.Equal(HttpStatusCode.NoContent, deleteResp.StatusCode);

        var getResp = await client.GetAsync($"/api/notifications/configs/{created.Id}");
        Assert.Equal(HttpStatusCode.NotFound, getResp.StatusCode);
    }

    [Fact]
    public async Task Unauthenticated_Returns401()
    {
        var client = _fixture.Factory.CreateClient();
        using var _ = client;

        var response = await client.GetAsync("/api/notifications/configs");

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }
}
