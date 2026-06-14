using System.Security.Claims;
using Mone.Api.Authorization;
using Mone.Api.Models;
using Mone.Api.Services;
using Mone.Contracts.Models;

namespace Mone.Api.Endpoints;

public static class PermissionEndpoints
{
    public static void MapPermissionEndpoints(this WebApplication app)
    {
        // Self-capabilities: available to ANY authenticated user (even with zero grants) so the
        // dashboard can render navigation. No RBAC permission required beyond authentication.
        app.MapGet("/api/auth/me/permissions", async (
            ClaimsPrincipal principal,
            IPermissionService permissions) =>
        {
            var userId = principal.FindFirstValue(ClaimTypes.NameIdentifier);
            if (userId is null) return Results.Unauthorized();

            var caps = await permissions.GetCapabilitiesAsync(userId);
            return Results.Ok(new MyPermissionsResponse(userId, caps));
        })
        .RequireAuthorization()
        .WithTags("Permissions")
        .WithName("GetMyPermissions")
        .WithSummary("Get the calling user's own effective capabilities. Available to any authenticated user.")
        .Produces<MyPermissionsResponse>();

        // Static catalog describing every resource and what it applies to. Drives the role editor.
        app.MapGet("/api/permissions/catalog", () => Results.Ok(Catalog))
            .RequireAuthorization()
            .RequirePermission(PermissionResource.Administration, PermissionLevel.View)
            .WithTags("Permissions")
            .WithName("GetPermissionCatalog")
            .WithSummary("Get the static catalog of permission resources, levels, and scope types that drives the role editor.")
            .Produces<PermissionCatalogResponse>();
    }

    private static readonly PermissionCatalogResponse Catalog = new(
        Resources:
        [
            new(PermissionResource.Hosts, "Host inventory.",
                "Creating, viewing, editing and deleting hosts (/api/hosts).", Scopeable: true),
            new(PermissionResource.Monitoring, "Live monitoring data.",
                "Status, probe results, metrics, the dashboard and notification history for hosts.", Scopeable: true),
            new(PermissionResource.Assignments, "Probe & checker assignments.",
                "Direct host assignments, group-level assignments, per-host overrides and manual probe triggers.", Scopeable: true),
            new(PermissionResource.Groups, "Host groups.",
                "Creating, editing, deleting host groups and managing their membership.", Scopeable: true),
            new(PermissionResource.Tags, "Tags.",
                "Defining tags and applying them to hosts. Managed globally.", Scopeable: false),
            new(PermissionResource.Notifications, "Notification channels.",
                "Notification channel configuration. Managed globally.", Scopeable: false),
            new(PermissionResource.Plugins, "Plugins.",
                "Plugin repositories, installed/loaded plugins and global plugin configuration. Managed globally.", Scopeable: false),
            new(PermissionResource.ExecutorNodes, "Executor nodes.",
                "Administration of remote executor nodes. Managed globally.", Scopeable: false),
            new(PermissionResource.System, "System maintenance.",
                "Housekeeping and data-retention operations. Managed globally.", Scopeable: false),
            new(PermissionResource.Administration, "Identity & access.",
                "Roles, users and role assignments (IAM). Managed globally.", Scopeable: false)
        ],
        Levels: [nameof(PermissionLevel.None), nameof(PermissionLevel.View), nameof(PermissionLevel.Manage)],
        ScopeTypes: [nameof(ScopeType.Global), nameof(ScopeType.Tag), nameof(ScopeType.Group)]);
}
