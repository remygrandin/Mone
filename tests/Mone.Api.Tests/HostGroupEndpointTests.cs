using System.Net;
using System.Net.Http.Json;
using Mone.Api.Models;
using Mone.Api.Tests.Fixtures;
using Xunit;

namespace Mone.Api.Tests;

[Collection("Api")]
public class HostGroupEndpointTests
{
    private readonly ApiFixture _fixture;

    public HostGroupEndpointTests(ApiFixture fixture) => _fixture = fixture;

    private async Task<(HttpClient Client, Guid HostId)> CreateClientAndHostAsync()
    {
        var client = await _fixture.CreateAuthenticatedClientAsync(
            $"hg_{Guid.NewGuid():N}@test.com", "ValidPass1!");

        var resp = await client.PostAsJsonAsync("/api/hosts",
            new CreateHostRequest($"host-{Guid.NewGuid():N}", "10.0.0.1"));
        var host = await resp.Content.ReadFromJsonAsync<HostResponse>();
        return (client, host!.Id);
    }

    private async Task<HostGroupResponse> CreateGroupAsync(HttpClient client, string? name = null, Guid? parentId = null)
    {
        var request = new CreateHostGroupRequest(name ?? $"grp-{Guid.NewGuid():N}", null, parentId);
        var resp = await client.PostAsJsonAsync("/api/host-groups", request);
        if (resp.StatusCode != HttpStatusCode.Created)
        {
            var body = await resp.Content.ReadAsStringAsync();
            Assert.Fail($"Expected 201 Created but got {(int)resp.StatusCode}: {body}");
        }
        var group = await resp.Content.ReadFromJsonAsync<HostGroupResponse>();
        Assert.NotNull(group);
        return group;
    }

    [Fact]
    public async Task CreateGroup_ValidData_Returns201()
    {
        using var client = await _fixture.CreateAuthenticatedClientAsync(
            $"hg_create_{Guid.NewGuid():N}@test.com", "ValidPass1!");

        var name = $"TestGroup-{Guid.NewGuid():N}";
        var group = await CreateGroupAsync(client, name);

        Assert.Equal(name, group.Name);
        Assert.NotEqual(Guid.Empty, group.Id);
        Assert.Null(group.ParentGroupId);
    }

    [Fact]
    public async Task CreateNestedGroups_VerifyParentChain()
    {
        using var client = await _fixture.CreateAuthenticatedClientAsync(
            $"hg_nested_{Guid.NewGuid():N}@test.com", "ValidPass1!");

        var a = await CreateGroupAsync(client, $"A-{Guid.NewGuid():N}");
        var b = await CreateGroupAsync(client, $"B-{Guid.NewGuid():N}", a.Id);
        var c = await CreateGroupAsync(client, $"C-{Guid.NewGuid():N}", b.Id);

        Assert.Null(a.ParentGroupId);
        Assert.Equal(a.Id, b.ParentGroupId);
        Assert.Equal(b.Id, c.ParentGroupId);
    }

    [Fact]
    public async Task UpdateGroup_CircularParent_Returns400()
    {
        using var client = await _fixture.CreateAuthenticatedClientAsync(
            $"hg_cycle_{Guid.NewGuid():N}@test.com", "ValidPass1!");

        var uid = Guid.NewGuid().ToString("N")[..8];
        var a = await CreateGroupAsync(client, $"A-{uid}");
        var b = await CreateGroupAsync(client, $"B-{uid}", a.Id);
        var c = await CreateGroupAsync(client, $"C-{uid}", b.Id);

        var resp = await client.PutAsJsonAsync($"/api/host-groups/{a.Id}",
            new UpdateHostGroupRequest($"A-{uid}", null, c.Id));

        Assert.Equal(HttpStatusCode.BadRequest, resp.StatusCode);
    }

    [Fact]
    public async Task UpdateGroup_SelfParent_Returns400()
    {
        using var client = await _fixture.CreateAuthenticatedClientAsync(
            $"hg_self_{Guid.NewGuid():N}@test.com", "ValidPass1!");

        var name = $"A-{Guid.NewGuid():N}";
        var a = await CreateGroupAsync(client, name);

        var resp = await client.PutAsJsonAsync($"/api/host-groups/{a.Id}",
            new UpdateHostGroupRequest(name, null, a.Id));

        Assert.Equal(HttpStatusCode.BadRequest, resp.StatusCode);
    }

    [Fact]
    public async Task AddMember_VerifyMembership()
    {
        var (client, hostId) = await CreateClientAndHostAsync();
        using var _ = client;

        var group = await CreateGroupAsync(client);

        var addResp = await client.PostAsJsonAsync($"/api/host-groups/{group.Id}/members",
            new AddGroupMemberRequest(hostId));
        Assert.Equal(HttpStatusCode.Created, addResp.StatusCode);

        var getResp = await client.GetAsync($"/api/host-groups/{group.Id}");
        var detail = await getResp.Content.ReadFromJsonAsync<HostGroupDetailResponse>();
        Assert.NotNull(detail);
        Assert.Single(detail.Members);
        Assert.Equal(hostId, detail.Members[0].HostId);
    }

    [Fact]
    public async Task RemoveMember_VerifyRemoval()
    {
        var (client, hostId) = await CreateClientAndHostAsync();
        using var _ = client;

        var group = await CreateGroupAsync(client);
        await client.PostAsJsonAsync($"/api/host-groups/{group.Id}/members",
            new AddGroupMemberRequest(hostId));

        var removeResp = await client.DeleteAsync($"/api/host-groups/{group.Id}/members/{hostId}");
        Assert.Equal(HttpStatusCode.NoContent, removeResp.StatusCode);

        var getResp = await client.GetAsync($"/api/host-groups/{group.Id}");
        var detail = await getResp.Content.ReadFromJsonAsync<HostGroupDetailResponse>();
        Assert.NotNull(detail);
        Assert.Empty(detail.Members);
    }

    [Fact]
    public async Task AddHostToTwoGroups_Succeeds()
    {
        var (client, hostId) = await CreateClientAndHostAsync();
        using var _ = client;

        var g1 = await CreateGroupAsync(client, "G1");
        var g2 = await CreateGroupAsync(client, "G2");

        var r1 = await client.PostAsJsonAsync($"/api/host-groups/{g1.Id}/members",
            new AddGroupMemberRequest(hostId));
        var r2 = await client.PostAsJsonAsync($"/api/host-groups/{g2.Id}/members",
            new AddGroupMemberRequest(hostId));

        Assert.Equal(HttpStatusCode.Created, r1.StatusCode);
        Assert.Equal(HttpStatusCode.Created, r2.StatusCode);
    }

    [Fact]
    public async Task DeleteGroup_NoChildrenNoMembers_Succeeds()
    {
        using var client = await _fixture.CreateAuthenticatedClientAsync(
            $"hg_del_{Guid.NewGuid():N}@test.com", "ValidPass1!");

        var group = await CreateGroupAsync(client);

        var resp = await client.DeleteAsync($"/api/host-groups/{group.Id}");
        Assert.Equal(HttpStatusCode.NoContent, resp.StatusCode);
    }

    [Fact]
    public async Task DeleteGroup_WithChildren_Returns400()
    {
        using var client = await _fixture.CreateAuthenticatedClientAsync(
            $"hg_delc_{Guid.NewGuid():N}@test.com", "ValidPass1!");

        var parent = await CreateGroupAsync(client, "Parent");
        await CreateGroupAsync(client, "Child", parent.Id);

        var resp = await client.DeleteAsync($"/api/host-groups/{parent.Id}");
        Assert.Equal(HttpStatusCode.BadRequest, resp.StatusCode);
    }

    [Fact]
    public async Task UpdateGroup_NameAndDescription()
    {
        using var client = await _fixture.CreateAuthenticatedClientAsync(
            $"hg_upd_{Guid.NewGuid():N}@test.com", "ValidPass1!");

        var group = await CreateGroupAsync(client, "Original");

        var resp = await client.PutAsJsonAsync($"/api/host-groups/{group.Id}",
            new UpdateHostGroupRequest("Updated", "A description", null));
        Assert.Equal(HttpStatusCode.OK, resp.StatusCode);

        var updated = await resp.Content.ReadFromJsonAsync<HostGroupResponse>();
        Assert.NotNull(updated);
        Assert.Equal("Updated", updated.Name);
        Assert.Equal("A description", updated.Description);
    }

    [Fact]
    public async Task ListGroups_ReturnsCounts()
    {
        var (client, hostId) = await CreateClientAndHostAsync();
        using var _ = client;

        var uniqueName = $"list-{Guid.NewGuid():N}";
        var parent = await CreateGroupAsync(client, uniqueName);
        await CreateGroupAsync(client, $"child-{Guid.NewGuid():N}", parent.Id);
        await client.PostAsJsonAsync($"/api/host-groups/{parent.Id}/members",
            new AddGroupMemberRequest(hostId));

        var resp = await client.GetAsync("/api/host-groups");
        Assert.Equal(HttpStatusCode.OK, resp.StatusCode);

        var groups = await resp.Content.ReadFromJsonAsync<HostGroupResponse[]>();
        Assert.NotNull(groups);

        var found = Assert.Single(groups, g => g.Name == uniqueName);
        Assert.Equal(1, found.MemberCount);
        Assert.Equal(1, found.ChildGroupCount);
    }
}
