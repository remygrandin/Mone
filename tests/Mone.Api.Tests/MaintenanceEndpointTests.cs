using System.Net;
using System.Net.Http.Json;
using Mone.Api.Models;
using Mone.Api.Tests.Fixtures;
using Mone.Infrastructure.Data.Entities;
using Xunit;

namespace Mone.Api.Tests;

[Collection("Api")]
public class MaintenanceEndpointTests
{
    private readonly ApiFixture _fixture;

    public MaintenanceEndpointTests(ApiFixture fixture) => _fixture = fixture;

    private async Task<(HttpClient client, Guid hostId)> CreateHostAsync(string emailPrefix)
    {
        var client = await _fixture.CreateAuthenticatedClientAsync(
            $"{emailPrefix}_{Guid.NewGuid():N}@test.com", "ValidPass1!", TestContext.Current.CancellationToken);

        var hostResp = await client.PostAsJsonAsync("/api/hosts",
            new CreateHostRequest($"maint-host-{Guid.NewGuid():N}", "10.0.0.1"), TestContext.Current.CancellationToken);
        var host = await hostResp.Content.ReadFromJsonAsync<HostResponse>(cancellationToken: TestContext.Current.CancellationToken);
        return (client, host!.Id);
    }

    [Fact]
    public async Task CreateManualWindow_Returns201WithComputedExpiry()
    {
        var (client, hostId) = await CreateHostAsync("maint_manual");
        using var c = client;

        var startsAt = DateTimeOffset.UtcNow;
        var response = await c.PostAsJsonAsync($"/api/hosts/{hostId}/maintenance",
            new CreateMaintenanceWindowRequest(MaintenanceWindowKind.Manual, startsAt, 5), TestContext.Current.CancellationToken);

        Assert.Equal(HttpStatusCode.Created, response.StatusCode);

        var window = await response.Content.ReadFromJsonAsync<MaintenanceWindowResponse>(cancellationToken: TestContext.Current.CancellationToken);
        Assert.NotNull(window);
        Assert.NotEqual(Guid.Empty, window.Id);
        Assert.Equal(hostId, window.HostId);
        Assert.Equal(MaintenanceWindowKind.Manual, window.Kind);
        Assert.NotNull(window.ExpiresAt);
        Assert.Equal(startsAt.AddMinutes(5), window.ExpiresAt!.Value, TimeSpan.FromSeconds(1));
    }

    [Fact]
    public async Task CreateManualWindow_OpenEnded_HasNullExpiry()
    {
        var (client, hostId) = await CreateHostAsync("maint_open");
        using var c = client;

        var response = await c.PostAsJsonAsync($"/api/hosts/{hostId}/maintenance",
            new CreateMaintenanceWindowRequest(MaintenanceWindowKind.Manual, DateTimeOffset.UtcNow, 0), TestContext.Current.CancellationToken);

        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
        var window = await response.Content.ReadFromJsonAsync<MaintenanceWindowResponse>(cancellationToken: TestContext.Current.CancellationToken);
        Assert.NotNull(window);
        Assert.Null(window.ExpiresAt);
    }

    [Fact]
    public async Task CreateRecurringWindow_ValidCron_Returns201()
    {
        var (client, hostId) = await CreateHostAsync("maint_cron_ok");
        using var c = client;

        var response = await c.PostAsJsonAsync($"/api/hosts/{hostId}/maintenance",
            new CreateMaintenanceWindowRequest(MaintenanceWindowKind.RecurringCron, null, 30, "0 2 * * *"), TestContext.Current.CancellationToken);

        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
        var window = await response.Content.ReadFromJsonAsync<MaintenanceWindowResponse>(cancellationToken: TestContext.Current.CancellationToken);
        Assert.NotNull(window);
        Assert.Equal(MaintenanceWindowKind.RecurringCron, window.Kind);
        Assert.Equal("0 2 * * *", window.Cron);
        Assert.Equal(30, window.DurationMinutes);
    }

    [Fact]
    public async Task CreateRecurringWindow_InvalidCron_Returns400ProblemJson()
    {
        var (client, hostId) = await CreateHostAsync("maint_cron_bad");
        using var c = client;

        var response = await c.PostAsJsonAsync($"/api/hosts/{hostId}/maintenance",
            new CreateMaintenanceWindowRequest(MaintenanceWindowKind.RecurringCron, null, 30, "not-a-cron"), TestContext.Current.CancellationToken);

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        Assert.Equal("application/problem+json", response.Content.Headers.ContentType?.MediaType);
    }

    [Fact]
    public async Task CreateRecurringWindow_ZeroDuration_Returns400()
    {
        var (client, hostId) = await CreateHostAsync("maint_cron_dur");
        using var c = client;

        var response = await c.PostAsJsonAsync($"/api/hosts/{hostId}/maintenance",
            new CreateMaintenanceWindowRequest(MaintenanceWindowKind.RecurringCron, null, 0, "0 2 * * *"), TestContext.Current.CancellationToken);

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        Assert.Equal("application/problem+json", response.Content.Headers.ContentType?.MediaType);
    }

    [Fact]
    public async Task CreateWindow_UnknownHost_Returns404()
    {
        using var client = await _fixture.CreateAuthenticatedClientAsync(
            $"maint_404create_{Guid.NewGuid():N}@test.com", "ValidPass1!", TestContext.Current.CancellationToken);

        var response = await client.PostAsJsonAsync($"/api/hosts/{Guid.NewGuid()}/maintenance",
            new CreateMaintenanceWindowRequest(MaintenanceWindowKind.Manual, DateTimeOffset.UtcNow, 5), TestContext.Current.CancellationToken);

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
    }

    [Fact]
    public async Task ListWindows_UnknownHost_Returns404()
    {
        using var client = await _fixture.CreateAuthenticatedClientAsync(
            $"maint_404list_{Guid.NewGuid():N}@test.com", "ValidPass1!", TestContext.Current.CancellationToken);

        var response = await client.GetAsync($"/api/hosts/{Guid.NewGuid()}/maintenance", TestContext.Current.CancellationToken);

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
    }

    [Fact]
    public async Task ListWindows_ReturnsCreatedWindows()
    {
        var (client, hostId) = await CreateHostAsync("maint_list");
        using var c = client;

        await c.PostAsJsonAsync($"/api/hosts/{hostId}/maintenance",
            new CreateMaintenanceWindowRequest(MaintenanceWindowKind.Manual, DateTimeOffset.UtcNow, 5), TestContext.Current.CancellationToken);
        await c.PostAsJsonAsync($"/api/hosts/{hostId}/maintenance",
            new CreateMaintenanceWindowRequest(MaintenanceWindowKind.RecurringCron, null, 30, "0 2 * * *"), TestContext.Current.CancellationToken);

        var response = await c.GetAsync($"/api/hosts/{hostId}/maintenance", TestContext.Current.CancellationToken);
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);

        var windows = await response.Content.ReadFromJsonAsync<MaintenanceWindowResponse[]>(cancellationToken: TestContext.Current.CancellationToken);
        Assert.NotNull(windows);
        Assert.Equal(2, windows.Length);
        Assert.All(windows, w => Assert.Equal(hostId, w.HostId));
    }

    [Fact]
    public async Task DeleteWindow_Returns204ThenAbsentFromList()
    {
        var (client, hostId) = await CreateHostAsync("maint_del");
        using var c = client;

        var createResp = await c.PostAsJsonAsync($"/api/hosts/{hostId}/maintenance",
            new CreateMaintenanceWindowRequest(MaintenanceWindowKind.Manual, DateTimeOffset.UtcNow, 5), TestContext.Current.CancellationToken);
        var created = await createResp.Content.ReadFromJsonAsync<MaintenanceWindowResponse>(cancellationToken: TestContext.Current.CancellationToken);

        var deleteResp = await c.DeleteAsync($"/api/hosts/{hostId}/maintenance/{created!.Id}", TestContext.Current.CancellationToken);
        Assert.Equal(HttpStatusCode.NoContent, deleteResp.StatusCode);

        var listResp = await c.GetAsync($"/api/hosts/{hostId}/maintenance", TestContext.Current.CancellationToken);
        var windows = await listResp.Content.ReadFromJsonAsync<MaintenanceWindowResponse[]>(cancellationToken: TestContext.Current.CancellationToken);
        Assert.NotNull(windows);
        Assert.DoesNotContain(windows, w => w.Id == created.Id);
    }
}
