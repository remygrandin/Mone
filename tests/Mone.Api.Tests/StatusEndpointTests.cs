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
public class StatusEndpointTests
{
    private readonly ApiFixture _fixture;

    public StatusEndpointTests(ApiFixture fixture) => _fixture = fixture;

    [Fact]
    public async Task GetLatestStatus_WithSeededData_ReturnsLatestPerChecker()
    {
        using var client = await _fixture.CreateAuthenticatedClientAsync(
            $"status_latest_{Guid.NewGuid():N}@test.com", "ValidPass1!", TestContext.Current.CancellationToken);

        var hostResp = await client.PostAsJsonAsync("/api/hosts",
            new CreateHostRequest($"status-host-{Guid.NewGuid():N}", "10.0.0.1"), cancellationToken: TestContext.Current.CancellationToken);
        var host = await hostResp.Content.ReadFromJsonAsync<HostResponse>(cancellationToken: TestContext.Current.CancellationToken);
        var hostId = host!.Id;

        using var scope = _fixture.Factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<MoneDbContext>();

        var now = DateTimeOffset.UtcNow;
        db.StatusHistory.AddRange(
            new StatusHistoryEntity
            {
                Timestamp = now.AddMinutes(-10),
                TargetId = hostId,
                CheckerId = "checker-a",
                PreviousStatus = MonitoringStatus.Unknown,
                CurrentStatus = MonitoringStatus.Healthy
            },
            new StatusHistoryEntity
            {
                Timestamp = now.AddMinutes(-5),
                TargetId = hostId,
                CheckerId = "checker-a",
                PreviousStatus = MonitoringStatus.Healthy,
                CurrentStatus = MonitoringStatus.Degraded
            },
            new StatusHistoryEntity
            {
                Timestamp = now.AddMinutes(-3),
                TargetId = hostId,
                CheckerId = "checker-b",
                PreviousStatus = MonitoringStatus.Unknown,
                CurrentStatus = MonitoringStatus.Unhealthy
            });
        await db.SaveChangesAsync(TestContext.Current.CancellationToken);

        var response = await client.GetAsync($"/api/hosts/{hostId}/status/latest", TestContext.Current.CancellationToken);

        if (response.StatusCode == HttpStatusCode.InternalServerError)
        {
            var body = await response.Content.ReadAsStringAsync(TestContext.Current.CancellationToken);
            Assert.Fail($"Server returned 500: {body}");
        }

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);

        var statuses = await response.Content.ReadFromJsonAsync<StatusResponse[]>(cancellationToken: TestContext.Current.CancellationToken);
        Assert.NotNull(statuses);
        Assert.Equal(2, statuses.Length);

        var checkerA = Assert.Single(statuses, s => s.CheckerId == "checker-a");
        Assert.Equal(MonitoringStatus.Degraded, checkerA.CurrentStatus);

        var checkerB = Assert.Single(statuses, s => s.CheckerId == "checker-b");
        Assert.Equal(MonitoringStatus.Unhealthy, checkerB.CurrentStatus);
    }

    [Fact]
    public async Task GetStatusHistory_WithTimeRange_ReturnsFiltered()
    {
        using var client = await _fixture.CreateAuthenticatedClientAsync(
            $"status_history_{Guid.NewGuid():N}@test.com", "ValidPass1!", TestContext.Current.CancellationToken);

        var hostResp = await client.PostAsJsonAsync("/api/hosts",
            new CreateHostRequest($"history-host-{Guid.NewGuid():N}", "10.0.0.1"), cancellationToken: TestContext.Current.CancellationToken);
        var host = await hostResp.Content.ReadFromJsonAsync<HostResponse>(cancellationToken: TestContext.Current.CancellationToken);
        var hostId = host!.Id;

        using var scope = _fixture.Factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<MoneDbContext>();

        var now = DateTimeOffset.UtcNow;
        db.StatusHistory.AddRange(
            new StatusHistoryEntity
            {
                Timestamp = now.AddHours(-2),
                TargetId = hostId,
                CheckerId = "checker-x",
                PreviousStatus = MonitoringStatus.Unknown,
                CurrentStatus = MonitoringStatus.Healthy
            },
            new StatusHistoryEntity
            {
                Timestamp = now.AddMinutes(-30),
                TargetId = hostId,
                CheckerId = "checker-x",
                PreviousStatus = MonitoringStatus.Healthy,
                CurrentStatus = MonitoringStatus.Degraded
            });
        await db.SaveChangesAsync(TestContext.Current.CancellationToken);

        var from = Uri.EscapeDataString(now.AddHours(-1).ToString("O"));
        var to = Uri.EscapeDataString(now.ToString("O"));
        var response = await client.GetAsync($"/api/hosts/{hostId}/status/history?from={from}&to={to}", TestContext.Current.CancellationToken);

        if (response.StatusCode != HttpStatusCode.OK)
        {
            var body = await response.Content.ReadAsStringAsync(TestContext.Current.CancellationToken);
            Assert.Fail($"Expected 200 but got {response.StatusCode}: {body}");
        }

        var history = await response.Content.ReadFromJsonAsync<StatusResponse[]>(cancellationToken: TestContext.Current.CancellationToken);
        Assert.NotNull(history);
        Assert.Single(history);
        Assert.Equal(MonitoringStatus.Degraded, history[0].CurrentStatus);
    }
}
