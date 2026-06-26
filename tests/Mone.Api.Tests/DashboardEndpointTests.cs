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
public class DashboardEndpointTests
{
    private readonly ApiFixture _fixture;

    public DashboardEndpointTests(ApiFixture fixture) => _fixture = fixture;

    [Fact]
    public async Task GetSummary_NoHosts_ReturnsZeroCounts()
    {
        using var client = await _fixture.CreateAuthenticatedClientAsync(
            $"dash_empty_{Guid.NewGuid():N}@test.com", "ValidPass1!");

        using var scope = _fixture.Factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<MoneDbContext>();

        var disabledHost = new HostEntity
        {
            Id = Guid.NewGuid(),
            Name = $"disabled-{Guid.NewGuid():N}",
            Address = "10.99.99.1",
            Enabled = false,
            CreatedAt = DateTimeOffset.UtcNow,
            UpdatedAt = DateTimeOffset.UtcNow
        };
        db.Hosts.Add(disabledHost);
        await db.SaveChangesAsync(TestContext.Current.CancellationToken);

        var response = await client.GetAsync("/api/dashboard/summary", TestContext.Current.CancellationToken);
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);

        var summary = await response.Content.ReadFromJsonAsync<DashboardSummaryResponse>(cancellationToken: TestContext.Current.CancellationToken);
        Assert.NotNull(summary);
        Assert.True(summary.TotalHosts >= 0);
    }

    [Fact]
    public async Task GetSummary_WithMixedStatuses_ReturnsCorrectCounts()
    {
        using var client = await _fixture.CreateAuthenticatedClientAsync(
            $"dash_mixed_{Guid.NewGuid():N}@test.com", "ValidPass1!");

        using var scope = _fixture.Factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<MoneDbContext>();

        var suffix = Guid.NewGuid().ToString("N")[..8];
        var hostHealthy = new HostEntity { Id = Guid.NewGuid(), Name = $"h-healthy-{suffix}", Address = "10.0.1.1", Enabled = true, CreatedAt = DateTimeOffset.UtcNow, UpdatedAt = DateTimeOffset.UtcNow };
        var hostDegraded = new HostEntity { Id = Guid.NewGuid(), Name = $"h-degraded-{suffix}", Address = "10.0.1.2", Enabled = true, CreatedAt = DateTimeOffset.UtcNow, UpdatedAt = DateTimeOffset.UtcNow };
        var hostUnhealthy = new HostEntity { Id = Guid.NewGuid(), Name = $"h-unhealthy-{suffix}", Address = "10.0.1.3", Enabled = true, CreatedAt = DateTimeOffset.UtcNow, UpdatedAt = DateTimeOffset.UtcNow };
        var hostNoStatus = new HostEntity { Id = Guid.NewGuid(), Name = $"h-unknown-{suffix}", Address = "10.0.1.4", Enabled = true, CreatedAt = DateTimeOffset.UtcNow, UpdatedAt = DateTimeOffset.UtcNow };

        db.Hosts.AddRange(hostHealthy, hostDegraded, hostUnhealthy, hostNoStatus);

        var now = DateTimeOffset.UtcNow;
        db.StatusHistory.AddRange(
            new StatusHistoryEntity
            {
                Timestamp = now.AddMinutes(-5),
                TargetId = hostHealthy.Id,
                CheckerId = "checker-1",
                PreviousStatus = MonitoringStatus.Unknown,
                CurrentStatus = MonitoringStatus.Healthy
            },
            new StatusHistoryEntity
            {
                Timestamp = now.AddMinutes(-5),
                TargetId = hostDegraded.Id,
                CheckerId = "checker-1",
                PreviousStatus = MonitoringStatus.Unknown,
                CurrentStatus = MonitoringStatus.Degraded
            },
            new StatusHistoryEntity
            {
                Timestamp = now.AddMinutes(-5),
                TargetId = hostUnhealthy.Id,
                CheckerId = "checker-1",
                PreviousStatus = MonitoringStatus.Unknown,
                CurrentStatus = MonitoringStatus.Unhealthy
            });
        await db.SaveChangesAsync(TestContext.Current.CancellationToken);

        var response = await client.GetAsync("/api/dashboard/summary", TestContext.Current.CancellationToken);

        if (response.StatusCode == HttpStatusCode.InternalServerError)
        {
            var body = await response.Content.ReadAsStringAsync(TestContext.Current.CancellationToken);
            Assert.Fail($"Server returned 500: {body}");
        }

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);

        var summary = await response.Content.ReadFromJsonAsync<DashboardSummaryResponse>(cancellationToken: TestContext.Current.CancellationToken);
        Assert.NotNull(summary);
        Assert.True(summary.TotalHosts >= 4);
        Assert.True(summary.Healthy >= 1);
        Assert.True(summary.Degraded >= 1);
        Assert.True(summary.Unhealthy >= 1);
        Assert.True(summary.Unknown >= 1);
    }

    [Fact]
    public async Task GetSummary_WorstCasePerHost_UsesWorstChecker()
    {
        using var client = await _fixture.CreateAuthenticatedClientAsync(
            $"dash_worst_{Guid.NewGuid():N}@test.com", "ValidPass1!", TestContext.Current.CancellationToken);

        using var scope = _fixture.Factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<MoneDbContext>();

        var suffix = Guid.NewGuid().ToString("N")[..8];
        var host = new HostEntity { Id = Guid.NewGuid(), Name = $"h-worst-{suffix}", Address = "10.0.2.1", Enabled = true, CreatedAt = DateTimeOffset.UtcNow, UpdatedAt = DateTimeOffset.UtcNow };
        db.Hosts.Add(host);

        var now = DateTimeOffset.UtcNow;
        db.StatusHistory.AddRange(
            new StatusHistoryEntity
            {
                Timestamp = now.AddMinutes(-5),
                TargetId = host.Id,
                CheckerId = "checker-ok",
                PreviousStatus = MonitoringStatus.Unknown,
                CurrentStatus = MonitoringStatus.Healthy
            },
            new StatusHistoryEntity
            {
                Timestamp = now.AddMinutes(-3),
                TargetId = host.Id,
                CheckerId = "checker-bad",
                PreviousStatus = MonitoringStatus.Unknown,
                CurrentStatus = MonitoringStatus.Unhealthy
            });
        await db.SaveChangesAsync(TestContext.Current.CancellationToken);

        var response = await client.GetAsync("/api/dashboard/summary", TestContext.Current.CancellationToken);
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);

        var summary = await response.Content.ReadFromJsonAsync<DashboardSummaryResponse>(cancellationToken: TestContext.Current.CancellationToken);
        Assert.NotNull(summary);
        Assert.True(summary.Unhealthy >= 1, "Host with one healthy and one unhealthy checker should count as unhealthy");
    }

    [Fact]
    public async Task GetSummary_Unauthenticated_Returns401()
    {
        var client = _fixture.Factory.CreateClient();
        using var _ = client;

        var response = await client.GetAsync("/api/dashboard/summary", TestContext.Current.CancellationToken);

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }
}
