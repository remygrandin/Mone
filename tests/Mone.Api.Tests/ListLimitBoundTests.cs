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
public class ListLimitBoundTests
{
    private readonly ApiFixture _fixture;

    public ListLimitBoundTests(ApiFixture fixture) => _fixture = fixture;

    private async Task<(HttpClient client, Guid hostId)> CreateHostAsync(string label)
    {
        var client = await _fixture.CreateAuthenticatedClientAsync(
            $"{label}_{Guid.NewGuid():N}@test.com", "ValidPass1!");

        var hostResp = await client.PostAsJsonAsync("/api/hosts",
            new CreateHostRequest($"{label}-host-{Guid.NewGuid():N}", "10.0.0.1"));
        var host = await hostResp.Content.ReadFromJsonAsync<HostResponse>();
        return (client, host!.Id);
    }

    [Fact]
    public async Task ListProbeResults_WithLimit_ReturnsNewestN()
    {
        var (client, hostId) = await CreateHostAsync("results_limit");
        using var _ = client;

        using (var scope = _fixture.Factory.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<MoneDbContext>();
            var now = DateTimeOffset.UtcNow;
            for (var i = 0; i < 5; i++)
            {
                db.ProbeResults.Add(new ProbeResultEntity
                {
                    Timestamp = now.AddMinutes(-i),
                    TargetId = hostId,
                    ProbeId = $"probe-{i}",
                    Status = MonitoringStatus.Healthy,
                    Summary = $"summary-{i}",
                    DurationMs = i,
                    MetadataJson = null
                });
            }
            await db.SaveChangesAsync();
        }

        var response = await client.GetAsync($"/api/hosts/{hostId}/results?limit=2");
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);

        var results = await response.Content.ReadFromJsonAsync<ProbeResultResponse[]>();
        Assert.NotNull(results);
        Assert.Equal(2, results.Length);
        // Newest two by Timestamp: probe-0 (now) then probe-1 (now-1m).
        Assert.Equal("probe-0", results[0].ProbeId);
        Assert.Equal("probe-1", results[1].ProbeId);
    }

    [Fact]
    public async Task ListProbeResults_WithZeroLimit_ClampsToOne()
    {
        var (client, hostId) = await CreateHostAsync("results_clamp");
        using var _ = client;

        using (var scope = _fixture.Factory.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<MoneDbContext>();
            var now = DateTimeOffset.UtcNow;
            for (var i = 0; i < 5; i++)
            {
                db.ProbeResults.Add(new ProbeResultEntity
                {
                    Timestamp = now.AddMinutes(-i),
                    TargetId = hostId,
                    ProbeId = $"probe-{i}",
                    Status = MonitoringStatus.Healthy,
                    Summary = $"summary-{i}",
                    DurationMs = i,
                    MetadataJson = null
                });
            }
            await db.SaveChangesAsync();
        }

        var response = await client.GetAsync($"/api/hosts/{hostId}/results?limit=0");
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);

        var results = await response.Content.ReadFromJsonAsync<ProbeResultResponse[]>();
        Assert.NotNull(results);
        Assert.Single(results);
        Assert.Equal("probe-0", results[0].ProbeId);
    }

    [Fact]
    public async Task GetStatusHistory_WithLimit_ReturnsNewestN()
    {
        var (client, hostId) = await CreateHostAsync("history_limit");
        using var _ = client;

        using (var scope = _fixture.Factory.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<MoneDbContext>();
            var now = DateTimeOffset.UtcNow;
            for (var i = 0; i < 5; i++)
            {
                db.StatusHistory.Add(new StatusHistoryEntity
                {
                    Timestamp = now.AddMinutes(-i),
                    TargetId = hostId,
                    CheckerId = $"checker-{i}",
                    PreviousStatus = MonitoringStatus.Unknown,
                    CurrentStatus = MonitoringStatus.Healthy
                });
            }
            await db.SaveChangesAsync();
        }

        var response = await client.GetAsync($"/api/hosts/{hostId}/status/history?limit=2");
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);

        var history = await response.Content.ReadFromJsonAsync<StatusResponse[]>();
        Assert.NotNull(history);
        Assert.Equal(2, history.Length);
        Assert.Equal("checker-0", history[0].CheckerId);
        Assert.Equal("checker-1", history[1].CheckerId);
    }

    [Fact]
    public async Task ListNotificationAudit_WithLimit_ReturnsNewestN()
    {
        var (client, hostId) = await CreateHostAsync("audit_limit");
        using var _ = client;

        using (var scope = _fixture.Factory.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<MoneDbContext>();
            var now = DateTimeOffset.UtcNow;
            for (var i = 0; i < 5; i++)
            {
                db.NotificationAudit.Add(new NotificationAuditEntity
                {
                    Id = Guid.NewGuid(),
                    Timestamp = now.AddMinutes(-i),
                    TargetId = hostId,
                    CheckerId = $"checker-{i}",
                    NotificationPluginId = $"plugin-{i}",
                    DeliveryStatus = DeliveryStatus.Sent,
                    ErrorMessage = null,
                    RetryCount = 0,
                    PreviousMonitoringStatus = MonitoringStatus.Unknown,
                    CurrentMonitoringStatus = MonitoringStatus.Healthy
                });
            }
            await db.SaveChangesAsync();
        }

        var response = await client.GetAsync($"/api/notifications?targetId={hostId}&limit=2");
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);

        var audit = await response.Content.ReadFromJsonAsync<NotificationAuditResponse[]>();
        Assert.NotNull(audit);
        Assert.Equal(2, audit.Length);
        Assert.Equal("checker-0", audit[0].CheckerId);
        Assert.Equal("checker-1", audit[1].CheckerId);
    }
}
