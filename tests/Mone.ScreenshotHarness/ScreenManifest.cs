namespace Mone.ScreenshotHarness;

/// <summary>
/// Describes a single dashboard screen to capture. Routes with a host id use a
/// {id} placeholder that the capture step substitutes with a real host id.
/// </summary>
public record ScreenDefinition(string Name, string Route, bool RequiresAuth, bool RequiresHostId);

/// <summary>
/// The single authoritative list of dashboard screens in scope for screenshot capture.
/// Adding a page to the dashboard without a matching entry here fails ScreenManifestTests.
/// </summary>
public static class ScreenManifest
{
    public static readonly IReadOnlyList<ScreenDefinition> All = new List<ScreenDefinition>
    {
        new("Login", "/login", RequiresAuth: false, RequiresHostId: false),
        new("Index", "/", RequiresAuth: true, RequiresHostId: false),
        new("HostList", "/hosts", RequiresAuth: true, RequiresHostId: false),
        new("HostGroups", "/groups", RequiresAuth: true, RequiresHostId: false),
        new("HostDetail", "/hosts/{id}", RequiresAuth: true, RequiresHostId: true),
        new("StatusHistory", "/hosts/{id}/status", RequiresAuth: true, RequiresHostId: true),
        new("Nodes", "/nodes", RequiresAuth: true, RequiresHostId: false),
        new("NotificationConfigs", "/notifications", RequiresAuth: true, RequiresHostId: false),
        new("Plugins", "/plugins", RequiresAuth: true, RequiresHostId: false),
        new("Roles", "/admin/roles", RequiresAuth: true, RequiresHostId: false),
        new("Users", "/admin/users", RequiresAuth: true, RequiresHostId: false),
        new("Housekeeping", "/housekeeping", RequiresAuth: true, RequiresHostId: false),
    };
}
