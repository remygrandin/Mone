using Mone.Contracts.Models;

namespace Mone.Api.Authorization;

public static class PermissionEndpointExtensions
{
    /// <summary>
    /// Requires the caller to hold at least the given level (inferred from the HTTP verb when
    /// <paramref name="level"/> is null) for <paramref name="resource"/>, scoped to the route's
    /// target. Chain after <c>RequireAuthorization()</c>. Works on both route groups and handlers.
    /// </summary>
    public static TBuilder RequirePermission<TBuilder>(
        this TBuilder builder, PermissionResource resource, PermissionLevel? level = null)
        where TBuilder : IEndpointConventionBuilder
    {
        builder.AddEndpointFilter(new RequirePermissionFilter(resource, level));
        return builder;
    }
}
