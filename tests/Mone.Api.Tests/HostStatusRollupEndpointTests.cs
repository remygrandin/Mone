using System.Net;
using System.Net.Http.Json;
using Microsoft.Extensions.DependencyInjection;
using Mone.Api.Models;
using Mone.Api.Tests.Fixtures;
using Mone.Contracts.Models;
using Mone.Infrastructure.Data;
using Mone.Infrastructure.Data.Entities;
using Xunit;

namespace Mone.Api.Tests;

[Collection("Api")]
public class HostStatusRollupEndpointTests
{
    private readonly ApiFixture _fixture;

    public HostStatusRollupEndpointTests(ApiFixture fixture) => _fixture = fixture;

    [Fact]
    public async Task Rollup_WithN2_TransitionsDegradedThenUnhealthy()
    {
        using var client = await _fixture.CreateAuthenticatedClientAsync(
            $"rollup_n2_{Guid.NewGuid():N}@test.com", "ValidPass1!");

        var hostResp = await client.PostAsJsonAsync("/api/hosts",
            new CreateHostRequest($"rollup-host-{Guid.NewGuid():N}", "10.0.0.1"));
        var host = await hostResp.Content.ReadFromJsonAsync<HostResponse>();
        var hostId = host!.Id;

        var policyResp = await client.PutAsJsonAsync(
            $"/api/hosts/{hostId}/error-policy", new UpdateHostErrorPolicyRequest(2));
        Assert.Equal(HttpStatusCode.OK, policyResp.StatusCode);
        var policy = await policyResp.Content.ReadFromJsonAsync<HostErrorPolicyResponse>();
        Assert.Equal(2, policy!.ErrorThreshold);

        var now = DateTimeOffset.UtcNow;

        // One checker Unhealthy: errorCount (1) < N (2) => Degraded.
        using (var scope = _fixture.Factory.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<MoneDbContext>();
            db.StatusHistory.Add(new StatusHistoryEntity
            {
                Timestamp = now.AddMinutes(-5),
                TargetId = hostId,
                CheckerId = "checker-a",
                PreviousStatus = MonitoringStatus.Healthy,
                CurrentStatus = MonitoringStatus.Unhealthy
            });
            await db.SaveChangesAsync();
        }

        var degradedResp = await client.GetAsync($"/api/hosts/{hostId}/status/rollup");
        if (degradedResp.StatusCode != HttpStatusCode.OK)
        {
            var body = await degradedResp.Content.ReadAsStringAsync();
            Assert.Fail($"Expected 200 but got {degradedResp.StatusCode}: {body}");
        }
        var degraded = await degradedResp.Content.ReadFromJsonAsync<HostStatusRollupResponse>();
        Assert.NotNull(degraded);
        Assert.Equal(MonitoringStatus.Degraded, degraded.Status);
        Assert.Equal(2, degraded.ErrorThreshold);
        Assert.Equal(1, degraded.CheckerCount);
        Assert.Equal(1, degraded.ErrorCount);

        // Second checker Unhealthy: errorCount (2) >= N (2) => Unhealthy.
        using (var scope = _fixture.Factory.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<MoneDbContext>();
            db.StatusHistory.Add(new StatusHistoryEntity
            {
                Timestamp = now.AddMinutes(-3),
                TargetId = hostId,
                CheckerId = "checker-b",
                PreviousStatus = MonitoringStatus.Healthy,
                CurrentStatus = MonitoringStatus.Unhealthy
            });
            await db.SaveChangesAsync();
        }

        var unhealthyResp = await client.GetAsync($"/api/hosts/{hostId}/status/rollup");
        Assert.Equal(HttpStatusCode.OK, unhealthyResp.StatusCode);
        var unhealthy = await unhealthyResp.Content.ReadFromJsonAsync<HostStatusRollupResponse>();
        Assert.NotNull(unhealthy);
        Assert.Equal(MonitoringStatus.Unhealthy, unhealthy.Status);
        Assert.Equal(2, unhealthy.CheckerCount);
        Assert.Equal(2, unhealthy.ErrorCount);
    }

    [Fact]
    public async Task Rollup_ForUnknownHost_Returns404()
    {
        using var client = await _fixture.CreateAuthenticatedClientAsync(
            $"rollup_404_{Guid.NewGuid():N}@test.com", "ValidPass1!");

        var response = await client.GetAsync($"/api/hosts/{Guid.NewGuid()}/status/rollup");

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
    }

    [Fact]
    public async Task UpdateErrorPolicy_WithZeroThreshold_Returns400()
    {
        using var client = await _fixture.CreateAuthenticatedClientAsync(
            $"rollup_zero_{Guid.NewGuid():N}@test.com", "ValidPass1!");

        var hostResp = await client.PostAsJsonAsync("/api/hosts",
            new CreateHostRequest($"rollup-zero-host-{Guid.NewGuid():N}", "10.0.0.1"));
        var host = await hostResp.Content.ReadFromJsonAsync<HostResponse>();

        var response = await client.PutAsJsonAsync(
            $"/api/hosts/{host!.Id}/error-policy", new UpdateHostErrorPolicyRequest(0));

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }
}
