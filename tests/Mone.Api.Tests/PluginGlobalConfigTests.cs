using System.Net;
using System.Net.Http.Json;
using Mone.Api.Tests.Fixtures;
using Xunit;
using static Mone.Api.Endpoints.PluginGlobalConfigEndpoints;

namespace Mone.Api.Tests;

[Collection("Api")]
public class PluginGlobalConfigTests
{
    private readonly ApiFixture _fixture;

    public PluginGlobalConfigTests(ApiFixture fixture) => _fixture = fixture;

    [Fact]
    public async Task PutThenGet_RoundTrip()
    {
        using var client = await _fixture.CreateAuthenticatedClientAsync(
            $"gc_rt_{Guid.NewGuid():N}@test.com", "ValidPass1!");

        var pluginId = $"TestPlugin_{Guid.NewGuid():N}";
        var configJson = "{\"smtp_host\":\"mail.example.com\",\"port\":587}";

        var putResp = await client.PutAsJsonAsync(
            $"/api/plugins/{pluginId}/global-config",
            new UpsertPluginGlobalConfigRequest(configJson));
        Assert.Equal(HttpStatusCode.OK, putResp.StatusCode);

        var getResp = await client.GetAsync($"/api/plugins/{pluginId}/global-config");
        Assert.Equal(HttpStatusCode.OK, getResp.StatusCode);

        var config = await getResp.Content.ReadFromJsonAsync<PluginGlobalConfigResponse>();
        Assert.NotNull(config);
        Assert.Equal(pluginId, config.PluginId);
        Assert.Equal(configJson, config.ConfigJson);
    }

    [Fact]
    public async Task Get_UnknownPlugin_ReturnsEmptyObject()
    {
        using var client = await _fixture.CreateAuthenticatedClientAsync(
            $"gc_unk_{Guid.NewGuid():N}@test.com", "ValidPass1!");

        var pluginId = $"NonExistent_{Guid.NewGuid():N}";
        var response = await client.GetAsync($"/api/plugins/{pluginId}/global-config");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);

        var config = await response.Content.ReadFromJsonAsync<PluginGlobalConfigResponse>();
        Assert.NotNull(config);
        Assert.Equal("{}", config.ConfigJson);
    }

    [Fact]
    public async Task Put_Upsert_OverwritesPreviousValue()
    {
        using var client = await _fixture.CreateAuthenticatedClientAsync(
            $"gc_ups_{Guid.NewGuid():N}@test.com", "ValidPass1!");

        var pluginId = $"UpsertPlugin_{Guid.NewGuid():N}";

        await client.PutAsJsonAsync(
            $"/api/plugins/{pluginId}/global-config",
            new UpsertPluginGlobalConfigRequest("{\"key\":\"old\"}"));

        var putResp = await client.PutAsJsonAsync(
            $"/api/plugins/{pluginId}/global-config",
            new UpsertPluginGlobalConfigRequest("{\"key\":\"new\"}"));
        Assert.Equal(HttpStatusCode.OK, putResp.StatusCode);

        var getResp = await client.GetAsync($"/api/plugins/{pluginId}/global-config");
        var config = await getResp.Content.ReadFromJsonAsync<PluginGlobalConfigResponse>();
        Assert.NotNull(config);
        Assert.Equal("{\"key\":\"new\"}", config.ConfigJson);
    }
}
