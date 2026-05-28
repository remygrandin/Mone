using System.Net;
using System.Net.Http.Json;
using Mone.Api.Models;
using Mone.Api.Tests.Fixtures;
using Xunit;

namespace Mone.Api.Tests;

[Collection("Api")]
public class HostEndpointTests
{
    private readonly ApiFixture _fixture;

    public HostEndpointTests(ApiFixture fixture) => _fixture = fixture;

    [Fact]
    public async Task CreateHost_ValidData_Returns201()
    {
        using var client = await _fixture.CreateAuthenticatedClientAsync(
            $"host_create_{Guid.NewGuid():N}@test.com", "ValidPass1!");

        var response = await client.PostAsJsonAsync("/api/hosts",
            new CreateHostRequest($"host-{Guid.NewGuid():N}", "192.168.1.1"));

        Assert.Equal(HttpStatusCode.Created, response.StatusCode);

        var host = await response.Content.ReadFromJsonAsync<HostResponse>();
        Assert.NotNull(host);
        Assert.NotEqual(Guid.Empty, host.Id);
    }

    [Fact]
    public async Task GetHost_ById_ReturnsHostWithTags()
    {
        using var client = await _fixture.CreateAuthenticatedClientAsync(
            $"host_get_{Guid.NewGuid():N}@test.com", "ValidPass1!");

        var tagResp = await client.PostAsJsonAsync("/api/tags", new CreateTagRequest($"tag-{Guid.NewGuid():N}"));
        var tag = await tagResp.Content.ReadFromJsonAsync<TagResponse>();

        var createResp = await client.PostAsJsonAsync("/api/hosts",
            new CreateHostRequest($"host-{Guid.NewGuid():N}", "10.0.0.1", true, [tag!.Id]));
        var created = await createResp.Content.ReadFromJsonAsync<HostResponse>();

        var getResp = await client.GetAsync($"/api/hosts/{created!.Id}");
        Assert.Equal(HttpStatusCode.OK, getResp.StatusCode);

        var host = await getResp.Content.ReadFromJsonAsync<HostResponse>();
        Assert.NotNull(host);
        Assert.Single(host.Tags);
    }

    [Fact]
    public async Task ListHosts_ReturnsAll()
    {
        using var client = await _fixture.CreateAuthenticatedClientAsync(
            $"host_list_{Guid.NewGuid():N}@test.com", "ValidPass1!");

        await client.PostAsJsonAsync("/api/hosts",
            new CreateHostRequest($"list-host-{Guid.NewGuid():N}", "10.0.0.1"));

        var response = await client.GetAsync("/api/hosts");
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);

        var hosts = await response.Content.ReadFromJsonAsync<HostResponse[]>();
        Assert.NotNull(hosts);
        Assert.NotEmpty(hosts);
    }

    [Fact]
    public async Task ListHosts_WithTagFilter_ReturnsMatching()
    {
        using var client = await _fixture.CreateAuthenticatedClientAsync(
            $"host_filter_{Guid.NewGuid():N}@test.com", "ValidPass1!");

        var tagName = $"filter-{Guid.NewGuid():N}";
        var tagResp = await client.PostAsJsonAsync("/api/tags", new CreateTagRequest(tagName));
        var tag = await tagResp.Content.ReadFromJsonAsync<TagResponse>();

        var taggedName = $"tagged-{Guid.NewGuid():N}";
        await client.PostAsJsonAsync("/api/hosts",
            new CreateHostRequest(taggedName, "10.0.0.1", true, [tag!.Id]));
        await client.PostAsJsonAsync("/api/hosts",
            new CreateHostRequest($"untagged-{Guid.NewGuid():N}", "10.0.0.2"));

        var response = await client.GetAsync($"/api/hosts?tags={tagName}");
        var hosts = await response.Content.ReadFromJsonAsync<HostResponse[]>();

        Assert.NotNull(hosts);
        Assert.All(hosts, h => Assert.Contains(h.Tags, t => t.Name == tagName));
    }

    [Fact]
    public async Task UpdateHost_ValidData_Returns200()
    {
        using var client = await _fixture.CreateAuthenticatedClientAsync(
            $"host_update_{Guid.NewGuid():N}@test.com", "ValidPass1!");

        var createResp = await client.PostAsJsonAsync("/api/hosts",
            new CreateHostRequest($"update-host-{Guid.NewGuid():N}", "10.0.0.1"));
        var created = await createResp.Content.ReadFromJsonAsync<HostResponse>();

        var newName = $"updated-{Guid.NewGuid():N}";
        var updateResp = await client.PutAsJsonAsync($"/api/hosts/{created!.Id}",
            new UpdateHostRequest(newName, "10.0.0.2", false));

        Assert.Equal(HttpStatusCode.OK, updateResp.StatusCode);

        var updated = await updateResp.Content.ReadFromJsonAsync<HostResponse>();
        Assert.Equal(newName, updated!.Name);
        Assert.False(updated.Enabled);
    }

    [Fact]
    public async Task DeleteHost_Returns204()
    {
        using var client = await _fixture.CreateAuthenticatedClientAsync(
            $"host_del_{Guid.NewGuid():N}@test.com", "ValidPass1!");

        var createResp = await client.PostAsJsonAsync("/api/hosts",
            new CreateHostRequest($"del-host-{Guid.NewGuid():N}", "10.0.0.1"));
        var created = await createResp.Content.ReadFromJsonAsync<HostResponse>();

        var deleteResp = await client.DeleteAsync($"/api/hosts/{created!.Id}");
        Assert.Equal(HttpStatusCode.NoContent, deleteResp.StatusCode);

        var getResp = await client.GetAsync($"/api/hosts/{created.Id}");
        Assert.Equal(HttpStatusCode.NotFound, getResp.StatusCode);
    }

    [Fact]
    public async Task CreateHost_DuplicateName_Returns409()
    {
        using var client = await _fixture.CreateAuthenticatedClientAsync(
            $"host_dup_{Guid.NewGuid():N}@test.com", "ValidPass1!");

        var name = $"dup-host-{Guid.NewGuid():N}";
        await client.PostAsJsonAsync("/api/hosts", new CreateHostRequest(name, "10.0.0.1"));

        var response = await client.PostAsJsonAsync("/api/hosts", new CreateHostRequest(name, "10.0.0.2"));
        Assert.Equal(HttpStatusCode.Conflict, response.StatusCode);
    }
}
