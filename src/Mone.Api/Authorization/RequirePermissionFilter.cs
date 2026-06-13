using System.Security.Claims;
using Mone.Api.Services;
using Mone.Contracts.Models;

namespace Mone.Api.Authorization;

/// <summary>
/// Endpoint filter that enforces a scoped RBAC permission. Runs after <c>RequireAuthorization()</c>
/// (so 401 stays the middleware's job); resolves the required level from the HTTP verb (unless an
/// explicit level is given) and the scope target from the route, then checks the user's effective
/// level via <see cref="IPermissionService"/>. Returns 403 ProblemDetails when insufficient.
/// </summary>
public sealed class RequirePermissionFilter : IEndpointFilter
{
    private readonly PermissionResource _resource;
    private readonly PermissionLevel? _explicitLevel;

    public RequirePermissionFilter(PermissionResource resource, PermissionLevel? explicitLevel = null)
    {
        _resource = resource;
        _explicitLevel = explicitLevel;
    }

    public async ValueTask<object?> InvokeAsync(EndpointFilterInvocationContext context, EndpointFilterDelegate next)
    {
        var http = context.HttpContext;

        var userId = http.User.FindFirstValue(ClaimTypes.NameIdentifier);
        if (string.IsNullOrEmpty(userId))
            return Results.Unauthorized();

        var required = _explicitLevel ?? RequiredLevelForMethod(http.Request.Method);
        var target = ResolveTarget(_resource, http);

        var permissions = http.RequestServices.GetRequiredService<IPermissionService>();
        var effective = await permissions.GetEffectiveLevelAsync(userId, _resource, target, http.RequestAborted);

        if (effective < required)
        {
            return Results.Problem(
                title: "Forbidden",
                detail: $"This action requires {_resource}:{required} on the targeted resource.",
                statusCode: StatusCodes.Status403Forbidden);
        }

        return await next(context);
    }

    private static PermissionLevel RequiredLevelForMethod(string method) =>
        HttpMethods.IsGet(method) || HttpMethods.IsHead(method) || HttpMethods.IsOptions(method)
            ? PermissionLevel.View
            : PermissionLevel.Manage;

    private static ScopeTarget ResolveTarget(PermissionResource resource, HttpContext http)
    {
        var route = http.Request.RouteValues;

        switch (resource)
        {
            // Host-scoped resources: a host id in the route names the target host.
            case PermissionResource.Hosts:
            case PermissionResource.Monitoring:
                return TryGuid(route, "hostId", out var h) || TryGuid(route, "id", out h)
                    ? ScopeTarget.Host(h)
                    : ScopeTarget.Global;

            case PermissionResource.Groups:
                return TryGuid(route, "groupId", out var g) || TryGuid(route, "id", out g)
                    ? ScopeTarget.Group(g)
                    : ScopeTarget.Global;

            // Assignments live under either /api/hosts/{hostId} or /api/host-groups/{groupId}.
            case PermissionResource.Assignments:
                if (TryGuid(route, "hostId", out var ah)) return ScopeTarget.Host(ah);
                if (TryGuid(route, "groupId", out var ag)) return ScopeTarget.Group(ag);
                return ScopeTarget.Global;

            // Global/system resources never honor scoped grants.
            default:
                return ScopeTarget.Global;
        }
    }

    private static bool TryGuid(IReadOnlyDictionary<string, object?> route, string key, out Guid value)
    {
        if (route.TryGetValue(key, out var raw) && raw is not null && Guid.TryParse(raw.ToString(), out var parsed))
        {
            value = parsed;
            return true;
        }

        value = Guid.Empty;
        return false;
    }
}
