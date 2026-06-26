using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Microsoft.AspNetCore.Mvc;
using Mone.Api.Models;
using Mone.Api.Tests.Fixtures;
using Xunit;

namespace Mone.Api.Tests;

/// <summary>
/// M006 single-product acceptance proof (R031): traverses the core operator journey
/// — login, host list, host create, host detail composition, probe assignment with
/// manifest config, group + membership, group probe assignment, and override edit —
/// end-to-end through the real API in one flow, asserting uniform named-DTO success
/// shapes and an RFC-9457 ProblemDetails error. Under autonomous execution there is no
/// UI driver (D048), so this integration test is the falsifiable "reads as one product"
/// stand-in for the manual walkthrough.
/// </summary>
[Collection("Api")]
public class OperatorJourneyTests
{
    private readonly ApiFixture _fixture;

    public OperatorJourneyTests(ApiFixture fixture) => _fixture = fixture;

    private static async Task AssertProblemDetailsAsync(HttpResponseMessage response, HttpStatusCode expected, CancellationToken ct = default)
    {
        Assert.Equal(expected, response.StatusCode);
        Assert.Equal("application/problem+json", response.Content.Headers.ContentType?.MediaType);

        var problem = await response.Content.ReadFromJsonAsync<ProblemDetails>(cancellationToken: ct);
        Assert.NotNull(problem);
        Assert.Equal((int)expected, problem.Status);
    }

    [Fact]
    public async Task OperatorJourney_LoginToOverride_TraversesAsOneProduct()
    {
        var suffix = Guid.NewGuid().ToString("N");

        // 1. Login — authenticating issues a usable SuperAdmin bearer token.
        using var client = await _fixture.CreateAuthenticatedClientAsync(
            $"journey_{suffix}@test.com", "ValidPass1!");

        // 2. Host list — landing surface returns 200 and a JSON array.
        var listResp = await client.GetAsync("/api/hosts", TestContext.Current.CancellationToken);
        Assert.Equal(HttpStatusCode.OK, listResp.StatusCode);
        using (var listDoc = JsonDocument.Parse(await listResp.Content.ReadAsStringAsync(TestContext.Current.CancellationToken)))
            Assert.Equal(JsonValueKind.Array, listDoc.RootElement.ValueKind);

        // 3. Create host — 201 Created with the named host DTO.
        var hostName = $"journey-host-{suffix}";
        var createResp = await client.PostAsJsonAsync("/api/hosts",
            new CreateHostRequest(hostName, "10.0.0.1"), cancellationToken: TestContext.Current.CancellationToken);
        Assert.Equal(HttpStatusCode.Created, createResp.StatusCode);
        var host = await createResp.Content.ReadFromJsonAsync<HostResponse>(cancellationToken: TestContext.Current.CancellationToken);
        Assert.NotNull(host);
        Assert.Equal(hostName, host.Name);
        var hostId = host.Id;

        // 4. Host detail composition — the HostDetail page reads the host's probe
        //    assignments and latest status; both resolve 200 with array shapes.
        var probesResp = await client.GetAsync($"/api/hosts/{hostId}/probes", TestContext.Current.CancellationToken);
        Assert.Equal(HttpStatusCode.OK, probesResp.StatusCode);
        var seededProbes = await probesResp.Content.ReadFromJsonAsync<ProbeAssignmentResponse[]>(cancellationToken: TestContext.Current.CancellationToken);
        Assert.NotNull(seededProbes);
        Assert.Empty(seededProbes);

        var statusResp = await client.GetAsync($"/api/hosts/{hostId}/status/latest", TestContext.Current.CancellationToken);
        Assert.Equal(HttpStatusCode.OK, statusResp.StatusCode);
        using (var statusDoc = JsonDocument.Parse(await statusResp.Content.ReadAsStringAsync(TestContext.Current.CancellationToken)))
            Assert.Equal(JsonValueKind.Array, statusDoc.RootElement.ValueKind);

        // 5. Probe assignment with manifest config — POST a "ping" probe carrying a
        //    config dictionary (the manifest config form payload); 201 + named DTO.
        var directAssignResp = await client.PostAsJsonAsync($"/api/hosts/{hostId}/probes",
            new CreateProbeAssignmentRequest("ping", $"ping-{suffix}", "*/5 * * * *",
                ConfigJson: """{"timeout":30,"retries":3}"""), cancellationToken: TestContext.Current.CancellationToken);
        Assert.Equal(HttpStatusCode.Created, directAssignResp.StatusCode);
        var directAssignment = await directAssignResp.Content.ReadFromJsonAsync<ProbeAssignmentResponse>(cancellationToken: TestContext.Current.CancellationToken);
        Assert.NotNull(directAssignment);
        Assert.Equal("ping", directAssignment.ProbePluginId);
        Assert.Equal(hostId, directAssignment.HostId);

        // 6. Host group + member — create a group and add the host as a member.
        var groupResp = await client.PostAsJsonAsync("/api/host-groups",
            new CreateHostGroupRequest($"journey-grp-{suffix}", null, null), cancellationToken: TestContext.Current.CancellationToken);
        Assert.Equal(HttpStatusCode.Created, groupResp.StatusCode);
        var hostGroup = await groupResp.Content.ReadFromJsonAsync<HostGroupResponse>(cancellationToken: TestContext.Current.CancellationToken);
        Assert.NotNull(hostGroup);
        var groupId = hostGroup.Id;

        var memberResp = await client.PostAsJsonAsync($"/api/host-groups/{groupId}/members",
            new AddGroupMemberRequest(hostId), cancellationToken: TestContext.Current.CancellationToken);
        Assert.Equal(HttpStatusCode.Created, memberResp.StatusCode);

        // 7. Group probe assignment + override edit — assign a probe at the group level
        //    (inherited by the member host), then override its config on the host.
        var groupAssignResp = await client.PostAsJsonAsync($"/api/host-groups/{groupId}/probes",
            new CreateProbeAssignmentRequest("http", $"http-{suffix}", "*/10 * * * *",
                ConfigJson: """{"timeout":15}"""), cancellationToken: TestContext.Current.CancellationToken);
        Assert.Equal(HttpStatusCode.Created, groupAssignResp.StatusCode);
        var groupAssignment = await groupAssignResp.Content.ReadFromJsonAsync<ProbeAssignmentResponse>(cancellationToken: TestContext.Current.CancellationToken);
        Assert.NotNull(groupAssignment);

        var overrideResp = await client.PutAsJsonAsync(
            $"/api/hosts/{hostId}/overrides/probes/{groupAssignment.Id}",
            new UpsertOverrideRequest { ConfigJsonOverride = """{"timeout":45}""", IsDisabled = false }, cancellationToken: TestContext.Current.CancellationToken);
        Assert.Equal(HttpStatusCode.OK, overrideResp.StatusCode);

        // 8. Cross-surface error consistency — a representative client error at the host
        //    surface returns RFC-9457 problem+json with the matching status, proving the
        //    error contract is uniform with the success surfaces above.
        var badResp = await client.PostAsJsonAsync("/api/hosts",
            new CreateHostRequest("", "10.0.0.2"), cancellationToken: TestContext.Current.CancellationToken);
        await AssertProblemDetailsAsync(badResp, HttpStatusCode.BadRequest, TestContext.Current.CancellationToken);
    }
}
