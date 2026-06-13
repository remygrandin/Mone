using Mone.Dashboard.Models;

namespace Mone.Dashboard.Services;

/// <summary>
/// Caches the current user's coarse per-resource capabilities (MAX level held anywhere, ignoring
/// scope) for navigation and page gating. Per-item actions are still enforced server-side by scope.
/// </summary>
public sealed class PermissionState
{
    private readonly ApiClient _api;
    private Dictionary<PermissionResource, PermissionLevel> _capabilities = new();
    private bool _loaded;

    public PermissionState(ApiClient api)
    {
        _api = api;
    }

    public bool Loaded => _loaded;

    public async Task EnsureLoadedAsync(bool force = false)
    {
        if (_loaded && !force) return;

        var result = await _api.GetMyPermissionsAsync();
        _capabilities = result?.Capabilities ?? new();
        _loaded = true;
    }

    public void Reset()
    {
        _capabilities = new();
        _loaded = false;
    }

    public PermissionLevel LevelFor(PermissionResource resource) =>
        _capabilities.TryGetValue(resource, out var level) ? level : PermissionLevel.None;

    public bool Has(PermissionResource resource, PermissionLevel atLeast = PermissionLevel.View) =>
        LevelFor(resource) >= atLeast;

    public bool CanManage(PermissionResource resource) => Has(resource, PermissionLevel.Manage);
}
