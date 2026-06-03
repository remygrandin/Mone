using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Mone.Api.Models;
using Mone.Api.Tests.Fixtures;
using Mone.Infrastructure.Services;
using Xunit;

namespace Mone.Api.Tests;

[Collection("Api")]
public class AssignmentOverrideEndpointTests
{
    private static readonly JsonSerializerOptions JsonOpts = new() { PropertyNameCaseInsensitive = true };

    private const AssignmentSourceType Direct = AssignmentSourceType.Direct;
    private const AssignmentSourceType Inherited = AssignmentSourceType.Inherited;

    private readonly ApiFixture _fixture;

    public AssignmentOverrideEndpointTests(ApiFixture fixture) => _fixture = fixture;

    private async Task<(HttpClient Client, Guid HostId)> CreateClientAndHostAsync()
    {
        var client = await _fixture.CreateAuthenticatedClientAsync(
            $"ov_{Guid.NewGuid():N}@test.com", "ValidPass1!");

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
    public async Task ConfigOverride_MergesIntoInheritedProbeAssignment()
    {
        var (client, hostId) = await CreateClientAndHostAsync();
        using var _ = client;

        var group = await CreateGroupAsync(client);
        await AddMemberAsync(client, group.Id, hostId);

        var assignResp = await client.PostAsJsonAsync($"/api/host-groups/{group.Id}/probes",
            new CreateProbeAssignmentRequest("ping", $"ping-{Guid.NewGuid():N}", "*/5 * * * *", ConfigJson: """{"timeout":30,"retries":3}"""));
        Assert.Equal(HttpStatusCode.Created, assignResp.StatusCode);
        var assignment = await assignResp.Content.ReadFromJsonAsync<ProbeAssignmentResponse>(JsonOpts);

        var overrideResp = await client.PutAsJsonAsync(
            $"/api/hosts/{hostId}/overrides/probes/{assignment!.Id}",
            new UpsertOverrideRequest { ConfigJsonOverride = """{"timeout":60}""", IsDisabled = false });
        Assert.Equal(HttpStatusCode.OK, overrideResp.StatusCode);

        var effective = await GetEffectiveAssignmentsAsync(client, hostId);
        Assert.Single(effective.Probes);
        var config = JsonDocument.Parse(effective.Probes[0].ConfigJson!);
        Assert.Equal(60, config.RootElement.GetProperty("timeout").GetInt32());
        Assert.Equal(3, config.RootElement.GetProperty("retries").GetInt32());
    }

    [Fact]
    public async Task DisabledOverride_SuppressesInheritedAssignment()
    {
        var (client, hostId) = await CreateClientAndHostAsync();
        using var _ = client;

        var group = await CreateGroupAsync(client);
        await AddMemberAsync(client, group.Id, hostId);

        var assignResp = await client.PostAsJsonAsync($"/api/host-groups/{group.Id}/probes",
            new CreateProbeAssignmentRequest("ping", $"ping-{Guid.NewGuid():N}", "*/5 * * * *"));
        Assert.Equal(HttpStatusCode.Created, assignResp.StatusCode);
        var assignment = await assignResp.Content.ReadFromJsonAsync<ProbeAssignmentResponse>(JsonOpts);

        var overrideResp = await client.PutAsJsonAsync(
            $"/api/hosts/{hostId}/overrides/probes/{assignment!.Id}",
            new UpsertOverrideRequest { IsDisabled = true });
        Assert.Equal(HttpStatusCode.OK, overrideResp.StatusCode);

        var effective = await GetEffectiveAssignmentsAsync(client, hostId);
        Assert.Empty(effective.Probes);
    }

    [Fact]
    public async Task OverrideRemoval_RestoresOriginalInheritance()
    {
        var (client, hostId) = await CreateClientAndHostAsync();
        using var _ = client;

        var group = await CreateGroupAsync(client);
        await AddMemberAsync(client, group.Id, hostId);

        var assignResp = await client.PostAsJsonAsync($"/api/host-groups/{group.Id}/probes",
            new CreateProbeAssignmentRequest("ping", $"ping-{Guid.NewGuid():N}", "*/5 * * * *", ConfigJson: """{"timeout":30}"""));
        Assert.Equal(HttpStatusCode.Created, assignResp.StatusCode);
        var assignment = await assignResp.Content.ReadFromJsonAsync<ProbeAssignmentResponse>(JsonOpts);

        await client.PutAsJsonAsync(
            $"/api/hosts/{hostId}/overrides/probes/{assignment!.Id}",
            new UpsertOverrideRequest { ConfigJsonOverride = """{"timeout":99}""", IsDisabled = false });

        var deleteResp = await client.DeleteAsync($"/api/hosts/{hostId}/overrides/probes/{assignment.Id}");
        Assert.Equal(HttpStatusCode.NoContent, deleteResp.StatusCode);

        var effective = await GetEffectiveAssignmentsAsync(client, hostId);
        Assert.Single(effective.Probes);
        var config = JsonDocument.Parse(effective.Probes[0].ConfigJson!);
        Assert.Equal(30, config.RootElement.GetProperty("timeout").GetInt32());
    }

    [Fact]
    public async Task OverrideOnDirectAssignment_RejectedWith400()
    {
        var (client, hostId) = await CreateClientAndHostAsync();
        using var _ = client;

        var assignResp = await client.PostAsJsonAsync($"/api/hosts/{hostId}/probes",
            new CreateProbeAssignmentRequest("ping", $"ping-{Guid.NewGuid():N}", "*/5 * * * *"));
        Assert.Equal(HttpStatusCode.Created, assignResp.StatusCode);
        var assignment = await assignResp.Content.ReadFromJsonAsync<ProbeAssignmentResponse>(JsonOpts);

        var overrideResp = await client.PutAsJsonAsync(
            $"/api/hosts/{hostId}/overrides/probes/{assignment!.Id}",
            new UpsertOverrideRequest { ConfigJsonOverride = """{"timeout":60}""" });
        Assert.Equal(HttpStatusCode.BadRequest, overrideResp.StatusCode);
    }

    [Fact]
    public async Task CheckerConfigOverride_AppliedCorrectly()
    {
        var (client, hostId) = await CreateClientAndHostAsync();
        using var _ = client;

        var group = await CreateGroupAsync(client);
        await AddMemberAsync(client, group.Id, hostId);

        var assignResp = await client.PostAsJsonAsync($"/api/host-groups/{group.Id}/checkers",
            new CreateCheckerAssignmentRequest("cpu", ConfigJson: """{"threshold":80,"interval":60}"""));
        Assert.Equal(HttpStatusCode.Created, assignResp.StatusCode);
        var assignment = await assignResp.Content.ReadFromJsonAsync<CheckerAssignmentResponse>(JsonOpts);

        var overrideResp = await client.PutAsJsonAsync(
            $"/api/hosts/{hostId}/overrides/checkers/{assignment!.Id}",
            new UpsertOverrideRequest { ConfigJsonOverride = """{"threshold":95}""", IsDisabled = false });
        Assert.Equal(HttpStatusCode.OK, overrideResp.StatusCode);

        var effective = await GetEffectiveAssignmentsAsync(client, hostId);
        Assert.Single(effective.Checkers);
        var config = JsonDocument.Parse(effective.Checkers[0].ConfigJson!);
        Assert.Equal(95, config.RootElement.GetProperty("threshold").GetInt32());
        Assert.Equal(60, config.RootElement.GetProperty("interval").GetInt32());
    }

    [Fact]
    public async Task MixedScenario_OverrideDisableAndUntouched()
    {
        var (client, hostId) = await CreateClientAndHostAsync();
        using var _ = client;

        var group = await CreateGroupAsync(client);
        await AddMemberAsync(client, group.Id, hostId);

        // Probe 1: will be config-overridden
        var probe1Resp = await client.PostAsJsonAsync($"/api/host-groups/{group.Id}/probes",
            new CreateProbeAssignmentRequest("ping", $"ping-{Guid.NewGuid():N}", "*/5 * * * *", ConfigJson: """{"timeout":30}"""));
        Assert.Equal(HttpStatusCode.Created, probe1Resp.StatusCode);
        var probe1 = await probe1Resp.Content.ReadFromJsonAsync<ProbeAssignmentResponse>(JsonOpts);

        // Probe 2: will be disabled
        var probe2Resp = await client.PostAsJsonAsync($"/api/host-groups/{group.Id}/probes",
            new CreateProbeAssignmentRequest("http", $"http-{Guid.NewGuid():N}", "*/10 * * * *"));
        Assert.Equal(HttpStatusCode.Created, probe2Resp.StatusCode);
        var probe2 = await probe2Resp.Content.ReadFromJsonAsync<ProbeAssignmentResponse>(JsonOpts);

        // Probe 3: untouched — no override
        var probe3Resp = await client.PostAsJsonAsync($"/api/host-groups/{group.Id}/probes",
            new CreateProbeAssignmentRequest("dns", $"dns-{Guid.NewGuid():N}", "*/15 * * * *"));
        Assert.Equal(HttpStatusCode.Created, probe3Resp.StatusCode);

        // Override probe 1 config
        await client.PutAsJsonAsync(
            $"/api/hosts/{hostId}/overrides/probes/{probe1!.Id}",
            new UpsertOverrideRequest { ConfigJsonOverride = """{"timeout":99}""", IsDisabled = false });

        // Disable probe 2
        await client.PutAsJsonAsync(
            $"/api/hosts/{hostId}/overrides/probes/{probe2!.Id}",
            new UpsertOverrideRequest { IsDisabled = true });

        var effective = await GetEffectiveAssignmentsAsync(client, hostId);

        // Should have 2 probes: overridden ping + untouched dns. http is disabled.
        Assert.Equal(2, effective.Probes.Length);
        Assert.DoesNotContain(effective.Probes, p => p.ProbePluginId == "http");

        var ping = Assert.Single(effective.Probes, p => p.ProbePluginId == "ping");
        var pingConfig = JsonDocument.Parse(ping.ConfigJson!);
        Assert.Equal(99, pingConfig.RootElement.GetProperty("timeout").GetInt32());

        var dns = Assert.Single(effective.Probes, p => p.ProbePluginId == "dns");
        Assert.Equal(Inherited, dns.SourceType);
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
