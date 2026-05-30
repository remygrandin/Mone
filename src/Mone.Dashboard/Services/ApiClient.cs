using System.Net;
using System.Net.Http.Json;
using Microsoft.AspNetCore.Components;
using Mone.Dashboard.Models;

namespace Mone.Dashboard.Services;

public sealed class ApiClient
{
    private readonly HttpClient _http;
    private readonly NavigationManager _nav;

    public ApiClient(HttpClient http, NavigationManager nav)
    {
        _http = http;
        _nav = nav;
    }

    public async Task<HostResponse[]> GetHostsAsync(Guid[]? tagIds = null)
    {
        var url = "api/hosts";
        if (tagIds is { Length: > 0 })
            url += "?tags=" + string.Join(",", tagIds);

        return await GetAsync<HostResponse[]>(url) ?? [];
    }

    public async Task<HostResponse?> GetHostAsync(Guid id) =>
        await GetAsync<HostResponse>($"api/hosts/{id}");

    public async Task<HostResponse?> CreateHostAsync(CreateHostRequest request) =>
        await PostAsync<CreateHostRequest, HostResponse>("api/hosts", request);

    public async Task UpdateHostAsync(Guid id, UpdateHostRequest request) =>
        await PutAsync($"api/hosts/{id}", request);

    public async Task DeleteHostAsync(Guid id) =>
        await DeleteAsync($"api/hosts/{id}");

    public async Task<TagResponse[]> GetTagsAsync() =>
        await GetAsync<TagResponse[]>("api/tags") ?? [];

    public async Task<StatusResponse[]> GetLatestStatusAsync(Guid hostId) =>
        await GetAsync<StatusResponse[]>($"api/hosts/{hostId}/status/latest") ?? [];

    public async Task<StatusResponse[]> GetStatusHistoryAsync(Guid hostId, DateTimeOffset? from = null, DateTimeOffset? to = null)
    {
        var url = $"api/hosts/{hostId}/status/history";
        var queryParts = new List<string>();
        if (from.HasValue)
            queryParts.Add($"from={Uri.EscapeDataString(from.Value.ToString("O"))}");
        if (to.HasValue)
            queryParts.Add($"to={Uri.EscapeDataString(to.Value.ToString("O"))}");
        if (queryParts.Count > 0)
            url += "?" + string.Join("&", queryParts);

        return await GetAsync<StatusResponse[]>(url) ?? [];
    }

    public async Task<ProbeAssignmentResponse[]> GetProbeAssignmentsAsync(Guid hostId) =>
        await GetAsync<ProbeAssignmentResponse[]>($"api/hosts/{hostId}/probes") ?? [];

    public async Task<CheckerAssignmentResponse[]> GetCheckerAssignmentsAsync(Guid hostId) =>
        await GetAsync<CheckerAssignmentResponse[]>($"api/hosts/{hostId}/checkers") ?? [];

    public async Task<ProbeAssignmentResponse?> CreateProbeAssignmentAsync(Guid hostId, CreateProbeAssignmentRequest request) =>
        await PostAsync<CreateProbeAssignmentRequest, ProbeAssignmentResponse>($"api/hosts/{hostId}/probes", request);

    public async Task UpdateProbeAssignmentAsync(Guid hostId, Guid id, UpdateProbeAssignmentRequest request) =>
        await PutAsync($"api/hosts/{hostId}/probes/{id}", request);

    public async Task DeleteProbeAssignmentAsync(Guid hostId, Guid id) =>
        await DeleteAsync($"api/hosts/{hostId}/probes/{id}");

    public async Task<CheckerAssignmentResponse?> CreateCheckerAssignmentAsync(Guid hostId, CreateCheckerAssignmentRequest request) =>
        await PostAsync<CreateCheckerAssignmentRequest, CheckerAssignmentResponse>($"api/hosts/{hostId}/checkers", request);

    public async Task UpdateCheckerAssignmentAsync(Guid hostId, Guid id, UpdateCheckerAssignmentRequest request) =>
        await PutAsync($"api/hosts/{hostId}/checkers/{id}", request);

    public async Task DeleteCheckerAssignmentAsync(Guid hostId, Guid id) =>
        await DeleteAsync($"api/hosts/{hostId}/checkers/{id}");

    public async Task<ProbeResultResponse[]> GetProbeResultsAsync(Guid hostId, DateTimeOffset? from = null, DateTimeOffset? to = null, string? probeId = null)
    {
        var url = $"api/hosts/{hostId}/results";
        var queryParts = new List<string>();
        if (from.HasValue)
            queryParts.Add($"from={Uri.EscapeDataString(from.Value.ToString("O"))}");
        if (to.HasValue)
            queryParts.Add($"to={Uri.EscapeDataString(to.Value.ToString("O"))}");
        if (!string.IsNullOrEmpty(probeId))
            queryParts.Add($"probeId={Uri.EscapeDataString(probeId)}");
        if (queryParts.Count > 0)
            url += "?" + string.Join("&", queryParts);

        return await GetAsync<ProbeResultResponse[]>(url) ?? [];
    }

    public async Task<DashboardSummaryResponse?> GetDashboardSummaryAsync() =>
        await GetAsync<DashboardSummaryResponse>("api/dashboard/summary");

    public async Task<NotificationConfigResponse[]> GetNotificationConfigsAsync() =>
        await GetAsync<NotificationConfigResponse[]>("api/notifications/configs") ?? [];

    public async Task<NotificationConfigResponse?> CreateNotificationConfigAsync(CreateNotificationConfigRequest request) =>
        await PostAsync<CreateNotificationConfigRequest, NotificationConfigResponse>("api/notifications/configs", request);

    public async Task UpdateNotificationConfigAsync(Guid id, UpdateNotificationConfigRequest request) =>
        await PutAsync("api/notifications/configs/" + id, request);

    public async Task DeleteNotificationConfigAsync(Guid id) =>
        await DeleteAsync("api/notifications/configs/" + id);

    private async Task<T?> GetAsync<T>(string url)
    {
        var response = await _http.GetAsync(url);

        if (response.StatusCode == HttpStatusCode.Unauthorized)
        {
            _nav.NavigateTo("/login", forceLoad: true);
            return default;
        }

        response.EnsureSuccessStatusCode();
        return await response.Content.ReadFromJsonAsync<T>();
    }

    private async Task<TRes?> PostAsync<TReq, TRes>(string url, TReq body)
    {
        var response = await _http.PostAsJsonAsync(url, body);

        if (response.StatusCode == HttpStatusCode.Unauthorized)
        {
            _nav.NavigateTo("/login", forceLoad: true);
            return default;
        }

        response.EnsureSuccessStatusCode();
        if (response.Content.Headers.ContentLength is 0) return default;
        var payload = await response.Content.ReadAsStringAsync();
        if (string.IsNullOrWhiteSpace(payload)) return default;
        return System.Text.Json.JsonSerializer.Deserialize<TRes>(payload,
            new System.Text.Json.JsonSerializerOptions(System.Text.Json.JsonSerializerDefaults.Web));
    }

    private async Task PutAsync<TReq>(string url, TReq body)
    {
        var response = await _http.PutAsJsonAsync(url, body);

        if (response.StatusCode == HttpStatusCode.Unauthorized)
        {
            _nav.NavigateTo("/login", forceLoad: true);
            return;
        }

        response.EnsureSuccessStatusCode();
    }

    public async Task<LoadedPluginModel[]> GetLoadedPluginsAsync() =>
        await GetAsync<LoadedPluginModel[]>("api/loaded-plugins") ?? [];

    public async Task<HostGroupResponse[]> GetHostGroupsAsync() =>
        await GetAsync<HostGroupResponse[]>("api/host-groups") ?? [];

    public async Task<HostGroupDetailResponse?> GetHostGroupAsync(Guid id) =>
        await GetAsync<HostGroupDetailResponse>($"api/host-groups/{id}");

    public async Task<HostGroupResponse?> CreateHostGroupAsync(CreateHostGroupRequest request) =>
        await PostAsync<CreateHostGroupRequest, HostGroupResponse>("api/host-groups", request);

    public async Task UpdateHostGroupAsync(Guid id, UpdateHostGroupRequest request) =>
        await PutAsync($"api/host-groups/{id}", request);

    public async Task DeleteHostGroupAsync(Guid id) =>
        await DeleteAsync($"api/host-groups/{id}");

    public async Task<EffectiveAssignmentsResponse?> GetEffectiveAssignmentsAsync(Guid hostId) =>
        await GetAsync<EffectiveAssignmentsResponse>($"api/hosts/{hostId}/effective-assignments");

    public async Task<OverridesResponse?> GetOverridesAsync(Guid hostId) =>
        await GetAsync<OverridesResponse>($"api/hosts/{hostId}/overrides");

    public async Task<OverrideResponse?> UpsertProbeOverrideAsync(Guid hostId, Guid assignmentId, UpsertOverrideRequest request) =>
        await PutWithResponseAsync<UpsertOverrideRequest, OverrideResponse>($"api/hosts/{hostId}/overrides/probes/{assignmentId}", request);

    public async Task DeleteProbeOverrideAsync(Guid hostId, Guid assignmentId) =>
        await DeleteAsync($"api/hosts/{hostId}/overrides/probes/{assignmentId}");

    public async Task<OverrideResponse?> UpsertCheckerOverrideAsync(Guid hostId, Guid assignmentId, UpsertOverrideRequest request) =>
        await PutWithResponseAsync<UpsertOverrideRequest, OverrideResponse>($"api/hosts/{hostId}/overrides/checkers/{assignmentId}", request);

    public async Task DeleteCheckerOverrideAsync(Guid hostId, Guid assignmentId) =>
        await DeleteAsync($"api/hosts/{hostId}/overrides/checkers/{assignmentId}");

    public async Task<ProbeResultResponse[]> GetLatestProbeResultsPerProbeAsync(Guid hostId) =>
        await GetAsync<ProbeResultResponse[]>($"api/hosts/{hostId}/results/latest-per-probe") ?? [];

    public async Task<string[]> GetMetricKeysAsync(Guid hostId) =>
        await GetAsync<string[]>($"api/hosts/{hostId}/results/metric-keys") ?? [];

    public async Task AddGroupMemberAsync(Guid groupId, Guid hostId) =>
        await PostAsync<AddGroupMemberRequest, object?>($"api/host-groups/{groupId}/members", new AddGroupMemberRequest(hostId));

    public async Task RemoveGroupMemberAsync(Guid groupId, Guid hostId) =>
        await DeleteAsync($"api/host-groups/{groupId}/members/{hostId}");

    public async Task TriggerProbeAsync(Guid hostId, string probePluginId) =>
        await PostAsync<TriggerProbeRequest, object?>($"api/hosts/{hostId}/trigger-probe", new TriggerProbeRequest(probePluginId));

    public async Task<PluginRepositoryResponse[]> GetPluginRepositoriesAsync() =>
        await GetAsync<PluginRepositoryResponse[]>("api/plugin-repos") ?? [];

    public async Task<PluginRepositoryResponse?> AddPluginRepositoryAsync(AddRepositoryRequest request) =>
        await PostAsync<AddRepositoryRequest, PluginRepositoryResponse>("api/plugin-repos", request);

    public async Task DeletePluginRepositoryAsync(Guid id) =>
        await DeleteAsync($"api/plugin-repos/{id}");

    public async Task SyncPluginRepositoryAsync(Guid id) =>
        await PostAsync<object?, object?>($"api/plugin-repos/{id}/sync", null);

    public async Task SyncAllPluginRepositoriesAsync() =>
        await PostAsync<object?, object?>("api/plugin-repos/sync-all", null);

    public async Task<PluginCatalogResponse[]> GetPluginCatalogAsync() =>
        await GetAsync<PluginCatalogResponse[]>("api/plugins") ?? [];

    public async Task InstallPluginAsync(Guid manifestId) =>
        await PostAsync<InstallPluginRequest, object?>("api/plugins/install", new InstallPluginRequest(manifestId));

    public async Task UninstallPluginAsync(string name) =>
        await PostAsync<UninstallPluginRequest, object?>("api/plugins/uninstall", new UninstallPluginRequest(name));

    public async Task ReloadPluginsAsync() =>
        await PostAsync<object?, object?>("api/plugins/reload", null);

    public async Task<DbSizeReport?> GetDbSizeAsync() =>
        await GetAsync<DbSizeReport>("api/housekeeping/db-size");

    public async Task<HousekeepingReport?> AssessHousekeepingAsync() =>
        await PostAsync<object?, HousekeepingReport>("api/housekeeping/assess", null);

    public async Task<CleanupResult?> CleanupAsync(string key) =>
        await PostAsync<CleanupRequest, CleanupResult>("api/housekeeping/cleanup", new CleanupRequest(key));

    private async Task<TRes?> PutWithResponseAsync<TReq, TRes>(string url, TReq body)
    {
        var response = await _http.PutAsJsonAsync(url, body);

        if (response.StatusCode == HttpStatusCode.Unauthorized)
        {
            _nav.NavigateTo("/login", forceLoad: true);
            return default;
        }

        response.EnsureSuccessStatusCode();
        return await response.Content.ReadFromJsonAsync<TRes>();
    }

    private async Task DeleteAsync(string url)
    {
        var response = await _http.DeleteAsync(url);

        if (response.StatusCode == HttpStatusCode.Unauthorized)
        {
            _nav.NavigateTo("/login", forceLoad: true);
            return;
        }

        response.EnsureSuccessStatusCode();
    }
}
