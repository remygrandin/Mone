using System.Net;
using System.Net.Http.Json;
using Microsoft.Extensions.DependencyInjection;
using Mone.Api.Models;
using Mone.Api.Tests.Fixtures;
using Mone.Contracts.Models;
using Mone.Infrastructure.Data;
using Mone.Infrastructure.Data.Entities;
using Mone.Messaging;
using Mone.Messaging.Messages;
using NATS.Client.JetStream;
using NATS.Client.JetStream.Models;
using Xunit;

namespace Mone.Api.Tests;

[Collection("Api")]
public class ForceStatusEndpointTests
{
    private readonly ApiFixture _fixture;

    public ForceStatusEndpointTests(ApiFixture fixture) => _fixture = fixture;

    private async Task<(HttpClient Client, Guid HostId)> SetupHostWithCheckerAsync(string suffix)
    {
        var client = await _fixture.CreateAuthenticatedClientAsync(
            $"force_{suffix}_{Guid.NewGuid():N}@test.com", "ValidPass1!");

        var hostResp = await client.PostAsJsonAsync("/api/hosts",
            new CreateHostRequest($"force-host-{Guid.NewGuid():N}", "10.0.0.1"));
        var host = await hostResp.Content.ReadFromJsonAsync<HostResponse>();
        var hostId = host!.Id;

        var checkerResp = await client.PostAsJsonAsync($"/api/hosts/{hostId}/checkers",
            new CreateCheckerAssignmentRequest("threshold", "threshold"));
        Assert.Equal(HttpStatusCode.Created, checkerResp.StatusCode);

        return (client, hostId);
    }

    [Fact]
    public async Task ForceStatus_PublishesStatusChange_AndRollupReflectsIt()
    {
        var (client, hostId) = await SetupHostWithCheckerAsync("publish");
        using var _ = client;

        // Independent consumer to capture the published message, mirroring AlertDispatcherService.
        var js = _fixture.Factory.Services.GetRequiredService<INatsJSContext>();
        var consumer = await js.CreateOrUpdateConsumerAsync(
            MoneStreams.StatusChanges.StreamName,
            new ConsumerConfig
            {
                Name = "test-force-" + Guid.NewGuid().ToString("N"),
                DeliverPolicy = ConsumerConfigDeliverPolicy.New,
                AckPolicy = ConsumerConfigAckPolicy.Explicit,
                FilterSubject = "status.changes.>"
            });

        var forceResp = await client.PostAsJsonAsync($"/api/hosts/{hostId}/status/force",
            new ForceStatusRequest("threshold", MonitoringStatus.Unhealthy));
        Assert.Equal(HttpStatusCode.Accepted, forceResp.StatusCode);

        // Read one message off the stream with a bounded wait.
        using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(10));
        var msg = await consumer.NextAsync<StatusChangeMessage>(cancellationToken: cts.Token);
        Assert.NotNull(msg);
        Assert.NotNull(msg!.Data);
        Assert.Equal("threshold", msg.Data!.CheckerId);
        Assert.Equal(MonitoringStatus.Unhealthy, msg.Data.Change.CurrentStatus);
        await msg.AckAsync(cancellationToken: cts.Token);

        // Server rollup reflects the forced status (default ErrorPolicyThreshold=1 => 1 error => Unhealthy).
        var rollupResp = await client.GetAsync($"/api/hosts/{hostId}/status/rollup");
        Assert.Equal(HttpStatusCode.OK, rollupResp.StatusCode);
        var rollup = await rollupResp.Content.ReadFromJsonAsync<HostStatusRollupResponse>();
        Assert.NotNull(rollup);
        Assert.Equal(MonitoringStatus.Unhealthy, rollup.Status);
        Assert.Equal(1, rollup.ErrorCount);

        // StatusHistory row persisted for the forced transition.
        using var scope = _fixture.Factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<MoneDbContext>();
        var persisted = db.StatusHistory.Any(s =>
            s.TargetId == hostId &&
            s.CheckerId == "threshold" &&
            s.CurrentStatus == MonitoringStatus.Unhealthy);
        Assert.True(persisted, "Expected a StatusHistory row for the forced Unhealthy transition.");
    }

    [Fact]
    public async Task ForceStatus_WithInvalidStatus_Returns400()
    {
        var (client, hostId) = await SetupHostWithCheckerAsync("invalid");
        using var _ = client;

        var response = await client.PostAsJsonAsync($"/api/hosts/{hostId}/status/force",
            new { checkerPluginId = "threshold", status = 999 });

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    [Fact]
    public async Task ForceStatus_WithUnknownChecker_Returns400()
    {
        var client = await _fixture.CreateAuthenticatedClientAsync(
            $"force_unknown_{Guid.NewGuid():N}@test.com", "ValidPass1!");
        using var _ = client;

        var hostResp = await client.PostAsJsonAsync("/api/hosts",
            new CreateHostRequest($"force-host-{Guid.NewGuid():N}", "10.0.0.1"));
        var host = await hostResp.Content.ReadFromJsonAsync<HostResponse>();

        var response = await client.PostAsJsonAsync($"/api/hosts/{host!.Id}/status/force",
            new ForceStatusRequest("does-not-exist", MonitoringStatus.Unhealthy));

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }
}
