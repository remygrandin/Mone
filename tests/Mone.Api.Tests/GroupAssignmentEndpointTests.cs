using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Mone.Api.Models;
using Mone.Api.Tests.Fixtures;
using Mone.Infrastructure.Services;
using Xunit;

namespace Mone.Api.Tests;

[Collection("Api")]
public class GroupAssignmentEndpointTests
{
    private static readonly JsonSerializerOptions JsonOpts = new() { PropertyNameCaseInsensitive = true };

    private const AssignmentSourceType Direct = AssignmentSourceType.Direct;
    private const AssignmentSourceType Inherited = AssignmentSourceType.Inherited;

    private readonly ApiFixture _fixture;

    public GroupAssignmentEndpointTests(ApiFixture fixture) => _fixture = fixture;

    private async Task<(HttpClient Client, Guid HostId)> CreateClientAndHostAsync()
    {
        var client = await _fixture.CreateAuthenticatedClientAsync(
            $"ga_{Guid.NewGuid():N}@test.com", "ValidPass1!");

        var resp = await client.PostAsJsonAsync("/api/hosts",
            new CreateHostRequest($"host-{Guid.NewGuid():N}", "10.0.0.1"));
        resp.EnsureSuccessStatusCode();
        var host = await resp.Content.ReadFromJsonAsync<HostResponse>();
        return (client, host!.Id);
    }

    private async Task<HostGroupResponse> CreateGroupAsync(HttpClient client, string? name = null, Guid? parentId = null)
    {
        var request = new CreateHostGroupRequest(name ?? $"grp-{Guid.NewGuid():N}", null, parentId);
        var resp = await client.PostAsJsonAsync("/api/host-groups", request);
        Assert.Equal(HttpStatusCode.Created, resp.StatusCode);
        return (await resp.Content.ReadFromJsonAsync<HostGroupResponse>())!;
    }

    private async Task AddMemberAsync(HttpClient client, Guid groupId, Guid hostId)
    {
        var resp = await client.PostAsJsonAsync($"/api/host-groups/{groupId}/members",
            new AddGroupMemberRequest(hostId));
        Assert.Equal(HttpStatusCode.Created, resp.StatusCode);
    }

    private async Task<EffectiveAssignmentsDto> GetEffectiveAssignmentsAsync(HttpClient client, Guid hostId)
    {
        var resp = await client.GetAsync($"/api/hosts/{hostId}/effective-assignments");
        Assert.Equal(HttpStatusCode.OK, resp.StatusCode);
        return (await resp.Content.ReadFromJsonAsync<EffectiveAssignmentsDto>(JsonOpts))!;
    }

    [Fact]
    public async Task GroupProbeAssignment_InheritedByMemberHost()
    {
        var (client, hostId) = await CreateClientAndHostAsync();
        using var _ = client;

        var group = await CreateGroupAsync(client);
        await AddMemberAsync(client, group.Id, hostId);

        var assignResp = await client.PostAsJsonAsync($"/api/host-groups/{group.Id}/probes",
            new CreateProbeAssignmentRequest("ping", $"ping-{Guid.NewGuid():N}", "*/5 * * * *"));
        Assert.Equal(HttpStatusCode.Created, assignResp.StatusCode);

        var effective = await GetEffectiveAssignmentsAsync(client, hostId);
        Assert.Single(effective.Probes);
        Assert.Equal("ping", effective.Probes[0].ProbePluginId);
        Assert.Equal(Inherited, effective.Probes[0].SourceType);
        Assert.Equal(group.Id, effective.Probes[0].SourceGroupId);
    }

    [Fact]
    public async Task NestedGroupProbeAssignment_InheritedByChildMemberHost()
    {
        var (client, hostId) = await CreateClientAndHostAsync();
        using var _ = client;

        var parent = await CreateGroupAsync(client, $"parent-{Guid.NewGuid():N}");
        var child = await CreateGroupAsync(client, $"child-{Guid.NewGuid():N}", parent.Id);
        await AddMemberAsync(client, child.Id, hostId);

        var assignResp = await client.PostAsJsonAsync($"/api/host-groups/{parent.Id}/probes",
            new CreateProbeAssignmentRequest("http", $"http-{Guid.NewGuid():N}", "*/10 * * * *"));
        Assert.Equal(HttpStatusCode.Created, assignResp.StatusCode);

        var effective = await GetEffectiveAssignmentsAsync(client, hostId);
        Assert.Single(effective.Probes);
        Assert.Equal("http", effective.Probes[0].ProbePluginId);
        Assert.Equal(Inherited, effective.Probes[0].SourceType);
        Assert.Equal(parent.Id, effective.Probes[0].SourceGroupId);
    }

    [Fact]
    public async Task ProbeWithTargetAddressOverride_ShownInEffective()
    {
        var (client, hostId) = await CreateClientAndHostAsync();
        using var _ = client;

        var assignResp = await client.PostAsJsonAsync($"/api/hosts/{hostId}/probes",
            new CreateProbeAssignmentRequest("http", $"http-{Guid.NewGuid():N}", "*/5 * * * *", TargetAddressOverride: "192.168.1.99"));
        Assert.Equal(HttpStatusCode.Created, assignResp.StatusCode);

        var effective = await GetEffectiveAssignmentsAsync(client, hostId);
        Assert.Single(effective.Probes);
        Assert.Equal("192.168.1.99", effective.Probes[0].TargetAddressOverride);
        Assert.Equal(Direct, effective.Probes[0].SourceType);
    }

    [Fact]
    public async Task DirectAndGroupAssignments_BothAppearInEffective()
    {
        var (client, hostId) = await CreateClientAndHostAsync();
        using var _ = client;

        var group = await CreateGroupAsync(client);
        await AddMemberAsync(client, group.Id, hostId);

        await client.PostAsJsonAsync($"/api/hosts/{hostId}/probes",
            new CreateProbeAssignmentRequest("ping", $"ping-{Guid.NewGuid():N}", "*/5 * * * *"));
        await client.PostAsJsonAsync($"/api/host-groups/{group.Id}/probes",
            new CreateProbeAssignmentRequest("http", $"http-{Guid.NewGuid():N}", "*/10 * * * *"));

        var effective = await GetEffectiveAssignmentsAsync(client, hostId);
        Assert.Equal(2, effective.Probes.Length);
        Assert.Contains(effective.Probes, p => p.ProbePluginId == "ping" && p.SourceType == Direct);
        Assert.Contains(effective.Probes, p => p.ProbePluginId == "http" && p.SourceType == Inherited);
    }

    [Fact]
    public async Task GroupCheckerAssignment_InheritedByMemberHost()
    {
        var (client, hostId) = await CreateClientAndHostAsync();
        using var _ = client;

        var group = await CreateGroupAsync(client);
        await AddMemberAsync(client, group.Id, hostId);

        var assignResp = await client.PostAsJsonAsync($"/api/host-groups/{group.Id}/checkers",
            new CreateCheckerAssignmentRequest("cpu", "cpu"));
        Assert.Equal(HttpStatusCode.Created, assignResp.StatusCode);

        var effective = await GetEffectiveAssignmentsAsync(client, hostId);
        Assert.Single(effective.Checkers);
        Assert.Equal("cpu", effective.Checkers[0].CheckerPluginId);
        Assert.Equal(Inherited, effective.Checkers[0].SourceType);
        Assert.Equal(group.Id, effective.Checkers[0].SourceGroupId);
    }

    [Fact]
    public async Task RemoveHostFromGroup_InheritedAssignmentsDisappear()
    {
        var (client, hostId) = await CreateClientAndHostAsync();
        using var _ = client;

        var group = await CreateGroupAsync(client);
        await AddMemberAsync(client, group.Id, hostId);

        await client.PostAsJsonAsync($"/api/host-groups/{group.Id}/probes",
            new CreateProbeAssignmentRequest("ping", $"ping-{Guid.NewGuid():N}", "*/5 * * * *"));

        var before = await GetEffectiveAssignmentsAsync(client, hostId);
        Assert.Single(before.Probes);

        var removeResp = await client.DeleteAsync($"/api/host-groups/{group.Id}/members/{hostId}");
        Assert.Equal(HttpStatusCode.NoContent, removeResp.StatusCode);

        var after = await GetEffectiveAssignmentsAsync(client, hostId);
        Assert.Empty(after.Probes);
    }

    private sealed class EffectiveAssignmentsDto
    {
        public EffectiveProbeDto[] Probes { get; set; } = [];
        public EffectiveCheckerDto[] Checkers { get; set; } = [];
    }

    private sealed class EffectiveProbeDto
    {
        public Guid AssignmentId { get; set; }
        public string ProbePluginId { get; set; } = "";
        public string ScheduleCron { get; set; } = "";
        public string? ConfigJson { get; set; }
        public string? TargetAddressOverride { get; set; }
        public bool Enabled { get; set; }
        public AssignmentSourceType SourceType { get; set; }
        public Guid? SourceGroupId { get; set; }
    }

    private sealed class EffectiveCheckerDto
    {
        public Guid AssignmentId { get; set; }
        public string CheckerPluginId { get; set; } = "";
        public string? ConfigJson { get; set; }
        public bool Enabled { get; set; }
        public AssignmentSourceType SourceType { get; set; }
        public Guid? SourceGroupId { get; set; }
    }
}
