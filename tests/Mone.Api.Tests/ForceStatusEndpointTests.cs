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

    private async Task<(HttpClient Client, Guid HostId, Guid AssignmentId)> SetupHostWithCheckerAsync(string suffix)
    {
        var client = await _fixture.CreateAuthenticatedClientAsync(
            $"force_{suffix}_{Guid.NewGuid():N}@test.com", "ValidPass1!", TestContext.Current.CancellationToken);

        var hostResp = await client.PostAsJsonAsync("/api/hosts",
            new CreateHostRequest($"force-host-{Guid.NewGuid():N}", "10.0.0.1"), cancellationToken: TestContext.Current.CancellationToken);
        var host = await hostResp.Content.ReadFromJsonAsync<HostResponse>(cancellationToken: TestContext.Current.CancellationToken);
        var hostId = host!.Id;

        var checkerResp = await client.PostAsJsonAsync($"/api/hosts/{hostId}/checkers",
            new CreateCheckerAssignmentRequest("threshold", "threshold"), cancellationToken: TestContext.Current.CancellationToken);
        Assert.Equal(HttpStatusCode.Created, checkerResp.StatusCode);
        var checkerAssignment = await checkerResp.Content.ReadFromJsonAsync<CheckerAssignmentResponse>(cancellationToken: TestContext.Current.CancellationToken);
        var assignmentId = checkerAssignment!.Id;

        return (client, hostId, assignmentId);
    }

    [Fact]
    public async Task ForceStatus_PublishesStatusChange_AndRollupReflectsIt()
    {
        var (client, hostId, assignmentId) = await SetupHostWithCheckerAsync("publish");
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
            }, cancellationToken: TestContext.Current.CancellationToken);

        var forceResp = await client.PostAsJsonAsync($"/api/hosts/{hostId}/status/force",
            new ForceStatusRequest(assignmentId, MonitoringStatus.Unhealthy), cancellationToken: TestContext.Current.CancellationToken);
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
        var rollupResp = await client.GetAsync($"/api/hosts/{hostId}/status/rollup", TestContext.Current.CancellationToken);
        Assert.Equal(HttpStatusCode.OK, rollupResp.StatusCode);
        var rollup = await rollupResp.Content.ReadFromJsonAsync<HostStatusRollupResponse>(cancellationToken: TestContext.Current.CancellationToken);
        Assert.NotNull(rollup);
        Assert.Equal(MonitoringStatus.Unhealthy, rollup.Status);
        Assert.Equal(1, rollup.ErrorCount);

        // StatusHistory row persisted for the forced transition.
        using var scope = _fixture.Factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<MoneDbContext>();
        var assignmentIdStr = assignmentId.ToString();
        var persisted = db.StatusHistory.Any(s =>
            s.TargetId == hostId &&
            s.CheckerId == assignmentIdStr &&
            s.CurrentStatus == MonitoringStatus.Unhealthy);
        Assert.True(persisted, "Expected a StatusHistory row for the forced Unhealthy transition.");
    }

    [Fact]
    public async Task ForceStatus_WithInvalidStatus_Returns400()
    {
        var (client, hostId, _) = await SetupHostWithCheckerAsync("invalid");
        using var _ = client;

        var response = await client.PostAsJsonAsync($"/api/hosts/{hostId}/status/force",
            new { assignmentId = Guid.NewGuid(), status = 999 }, cancellationToken: TestContext.Current.CancellationToken);

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    [Fact]
    public async Task ForceStatus_WithUnknownChecker_Returns400()
    {
        var client = await _fixture.CreateAuthenticatedClientAsync(
            $"force_unknown_{Guid.NewGuid():N}@test.com", "ValidPass1!", TestContext.Current.CancellationToken);
        using var _ = client;

        var hostResp = await client.PostAsJsonAsync("/api/hosts",
            new CreateHostRequest($"force-host-{Guid.NewGuid():N}", "10.0.0.1"), cancellationToken: TestContext.Current.CancellationToken);
        var host = await hostResp.Content.ReadFromJsonAsync<HostResponse>(cancellationToken: TestContext.Current.CancellationToken);

        var response = await client.PostAsJsonAsync($"/api/hosts/{host!.Id}/status/force",
            new ForceStatusRequest(Guid.NewGuid(), MonitoringStatus.Unhealthy), cancellationToken: TestContext.Current.CancellationToken);

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }
}
