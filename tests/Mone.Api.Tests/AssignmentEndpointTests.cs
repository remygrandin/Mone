using System.Net;
using System.Net.Http.Json;
using Mone.Api.Models;
using Mone.Api.Tests.Fixtures;
using Xunit;

namespace Mone.Api.Tests;

[Collection("Api")]
public class AssignmentEndpointTests
{
    private readonly ApiFixture _fixture;

    public AssignmentEndpointTests(ApiFixture fixture) => _fixture = fixture;

    private async Task<(HttpClient Client, Guid HostId)> SetupHostAsync()
    {
        var client = await _fixture.CreateAuthenticatedClientAsync(
            $"assign_{Guid.NewGuid():N}@test.com", "ValidPass1!");

        var resp = await client.PostAsJsonAsync("/api/hosts",
            new CreateHostRequest($"assign-host-{Guid.NewGuid():N}", "10.0.0.1"));
        var host = await resp.Content.ReadFromJsonAsync<HostResponse>();
        return (client, host!.Id);
    }

    [Fact]
    public async Task CreateProbeAssignment_Returns201()
    {
        var (client, hostId) = await SetupHostAsync();
        using var _ = client;

        var response = await client.PostAsJsonAsync($"/api/hosts/{hostId}/probes",
            new CreateProbeAssignmentRequest("ping", $"ping-{Guid.NewGuid():N}", "*/5 * * * *"));

        Assert.Equal(HttpStatusCode.Created, response.StatusCode);

        var assignment = await response.Content.ReadFromJsonAsync<ProbeAssignmentResponse>();
        Assert.NotNull(assignment);
        Assert.Equal(hostId, assignment.HostId);
        Assert.Equal("ping", assignment.ProbePluginId);
    }

    [Fact]
    public async Task ListProbeAssignments_ReturnsAssignments()
    {
        var (client, hostId) = await SetupHostAsync();
        using var _ = client;

        await client.PostAsJsonAsync($"/api/hosts/{hostId}/probes",
            new CreateProbeAssignmentRequest("ping", $"ping-{Guid.NewGuid():N}", "*/5 * * * *"));

        var response = await client.GetAsync($"/api/hosts/{hostId}/probes");
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);

        var assignments = await response.Content.ReadFromJsonAsync<ProbeAssignmentResponse[]>();
        Assert.NotNull(assignments);
        Assert.Single(assignments);
    }

    [Fact]
    public async Task CreateCheckerAssignment_Returns201()
    {
        var (client, hostId) = await SetupHostAsync();
        using var _ = client;

        var response = await client.PostAsJsonAsync($"/api/hosts/{hostId}/checkers",
            new CreateCheckerAssignmentRequest("threshold"));

        Assert.Equal(HttpStatusCode.Created, response.StatusCode);

        var assignment = await response.Content.ReadFromJsonAsync<CheckerAssignmentResponse>();
        Assert.NotNull(assignment);
        Assert.Equal(hostId, assignment.HostId);
        Assert.Equal("threshold", assignment.CheckerPluginId);
    }

    [Fact]
    public async Task DeleteProbeAssignment_Returns204()
    {
        var (client, hostId) = await SetupHostAsync();
        using var _ = client;

        var createResp = await client.PostAsJsonAsync($"/api/hosts/{hostId}/probes",
            new CreateProbeAssignmentRequest("ping", $"ping-{Guid.NewGuid():N}", "*/5 * * * *"));
        var assignment = await createResp.Content.ReadFromJsonAsync<ProbeAssignmentResponse>();

        var deleteResp = await client.DeleteAsync($"/api/hosts/{hostId}/probes/{assignment!.Id}");
        Assert.Equal(HttpStatusCode.NoContent, deleteResp.StatusCode);

        var listResp = await client.GetAsync($"/api/hosts/{hostId}/probes");
        var remaining = await listResp.Content.ReadFromJsonAsync<ProbeAssignmentResponse[]>();
        Assert.Empty(remaining!);
    }
}
