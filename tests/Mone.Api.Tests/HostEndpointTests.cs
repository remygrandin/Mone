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
            $"host_create_{Guid.NewGuid():N}@test.com", "ValidPass1!", TestContext.Current.CancellationToken);

        var response = await client.PostAsJsonAsync("/api/hosts",
            new CreateHostRequest($"host-{Guid.NewGuid():N}", "192.168.1.1"), cancellationToken: TestContext.Current.CancellationToken);

        Assert.Equal(HttpStatusCode.Created, response.StatusCode);

        var host = await response.Content.ReadFromJsonAsync<HostResponse>(cancellationToken: TestContext.Current.CancellationToken);
        Assert.NotNull(host);
        Assert.NotEqual(Guid.Empty, host.Id);
    }

    [Fact]
    public async Task GetHost_ById_ReturnsHostWithTags()
    {
        using var client = await _fixture.CreateAuthenticatedClientAsync(
            $"host_get_{Guid.NewGuid():N}@test.com", "ValidPass1!", TestContext.Current.CancellationToken);

        var tagResp = await client.PostAsJsonAsync("/api/tags", new CreateTagRequest($"tag-{Guid.NewGuid():N}"), cancellationToken: TestContext.Current.CancellationToken);
        var tag = await tagResp.Content.ReadFromJsonAsync<TagResponse>(cancellationToken: TestContext.Current.CancellationToken);

        var createResp = await client.PostAsJsonAsync("/api/hosts",
            new CreateHostRequest($"host-{Guid.NewGuid():N}", "10.0.0.1", true, [tag!.Id]), cancellationToken: TestContext.Current.CancellationToken);
        var created = await createResp.Content.ReadFromJsonAsync<HostResponse>(cancellationToken: TestContext.Current.CancellationToken);

        var getResp = await client.GetAsync($"/api/hosts/{created!.Id}", TestContext.Current.CancellationToken);
        Assert.Equal(HttpStatusCode.OK, getResp.StatusCode);

        var host = await getResp.Content.ReadFromJsonAsync<HostResponse>(cancellationToken: TestContext.Current.CancellationToken);
        Assert.NotNull(host);
        Assert.Single(host.Tags);
    }

    [Fact]
    public async Task ListHosts_ReturnsAll()
    {
        using var client = await _fixture.CreateAuthenticatedClientAsync(
            $"host_list_{Guid.NewGuid():N}@test.com", "ValidPass1!", TestContext.Current.CancellationToken);

        await client.PostAsJsonAsync("/api/hosts",
            new CreateHostRequest($"list-host-{Guid.NewGuid():N}", "10.0.0.1"), cancellationToken: TestContext.Current.CancellationToken);

        var response = await client.GetAsync("/api/hosts", TestContext.Current.CancellationToken);
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);

        var hosts = await response.Content.ReadFromJsonAsync<HostResponse[]>(cancellationToken: TestContext.Current.CancellationToken);
        Assert.NotNull(hosts);
        Assert.NotEmpty(hosts);
    }

    [Fact]
    public async Task ListHosts_WithTagFilter_ReturnsMatching()
    {
        using var client = await _fixture.CreateAuthenticatedClientAsync(
            $"host_filter_{Guid.NewGuid():N}@test.com", "ValidPass1!", TestContext.Current.CancellationToken);

        var tagName = $"filter-{Guid.NewGuid():N}";
        var tagResp = await client.PostAsJsonAsync("/api/tags", new CreateTagRequest(tagName), cancellationToken: TestContext.Current.CancellationToken);
        var tag = await tagResp.Content.ReadFromJsonAsync<TagResponse>(cancellationToken: TestContext.Current.CancellationToken);

        var taggedName = $"tagged-{Guid.NewGuid():N}";
        await client.PostAsJsonAsync("/api/hosts",
            new CreateHostRequest(taggedName, "10.0.0.1", true, [tag!.Id]), cancellationToken: TestContext.Current.CancellationToken);
        await client.PostAsJsonAsync("/api/hosts",
            new CreateHostRequest($"untagged-{Guid.NewGuid():N}", "10.0.0.2"), cancellationToken: TestContext.Current.CancellationToken);

        var response = await client.GetAsync($"/api/hosts?tags={tagName}", TestContext.Current.CancellationToken);
        var hosts = await response.Content.ReadFromJsonAsync<HostResponse[]>(cancellationToken: TestContext.Current.CancellationToken);

        Assert.NotNull(hosts);
        Assert.All(hosts, h => Assert.Contains(h.Tags, t => t.Name == tagName));
    }

    [Fact]
    public async Task UpdateHost_ValidData_Returns200()
    {
        using var client = await _fixture.CreateAuthenticatedClientAsync(
            $"host_update_{Guid.NewGuid():N}@test.com", "ValidPass1!", TestContext.Current.CancellationToken);

        var createResp = await client.PostAsJsonAsync("/api/hosts",
            new CreateHostRequest($"update-host-{Guid.NewGuid():N}", "10.0.0.1"), cancellationToken: TestContext.Current.CancellationToken);
        var created = await createResp.Content.ReadFromJsonAsync<HostResponse>(cancellationToken: TestContext.Current.CancellationToken);

        var newName = $"updated-{Guid.NewGuid():N}";
        var updateResp = await client.PutAsJsonAsync($"/api/hosts/{created!.Id}",
            new UpdateHostRequest(newName, "10.0.0.2", false), cancellationToken: TestContext.Current.CancellationToken);

        Assert.Equal(HttpStatusCode.OK, updateResp.StatusCode);

        var updated = await updateResp.Content.ReadFromJsonAsync<HostResponse>(cancellationToken: TestContext.Current.CancellationToken);
        Assert.Equal(newName, updated!.Name);
        Assert.False(updated.Enabled);
    }

    [Fact]
    public async Task DeleteHost_Returns204()
    {
        using var client = await _fixture.CreateAuthenticatedClientAsync(
            $"host_del_{Guid.NewGuid():N}@test.com", "ValidPass1!", TestContext.Current.CancellationToken);

        var createResp = await client.PostAsJsonAsync("/api/hosts",
            new CreateHostRequest($"del-host-{Guid.NewGuid():N}", "10.0.0.1"), cancellationToken: TestContext.Current.CancellationToken);
        var created = await createResp.Content.ReadFromJsonAsync<HostResponse>(cancellationToken: TestContext.Current.CancellationToken);

        var deleteResp = await client.DeleteAsync($"/api/hosts/{created!.Id}", TestContext.Current.CancellationToken);
        Assert.Equal(HttpStatusCode.NoContent, deleteResp.StatusCode);

        var getResp = await client.GetAsync($"/api/hosts/{created.Id}", TestContext.Current.CancellationToken);
        Assert.Equal(HttpStatusCode.NotFound, getResp.StatusCode);
    }

    [Fact]
    public async Task CreateHost_DuplicateName_Returns409()
    {
        using var client = await _fixture.CreateAuthenticatedClientAsync(
            $"host_dup_{Guid.NewGuid():N}@test.com", "ValidPass1!", TestContext.Current.CancellationToken);

        var name = $"dup-host-{Guid.NewGuid():N}";
        await client.PostAsJsonAsync("/api/hosts", new CreateHostRequest(name, "10.0.0.1"), cancellationToken: TestContext.Current.CancellationToken);

        var response = await client.PostAsJsonAsync("/api/hosts", new CreateHostRequest(name, "10.0.0.2"), cancellationToken: TestContext.Current.CancellationToken);
        Assert.Equal(HttpStatusCode.Conflict, response.StatusCode);
    }
}
