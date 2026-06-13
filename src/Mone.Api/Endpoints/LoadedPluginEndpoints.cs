using Mone.Api.Models;
using Mone.PluginEngine;

using Mone.Api.Authorization;
using Mone.Contracts.Models;

namespace Mone.Api.Endpoints;

public static class LoadedPluginEndpoints
{
    public static void MapLoadedPluginEndpoints(this WebApplication app)
    {
        var group = app.MapGroup("/api/loaded-plugins")
            .RequireAuthorization()
            .RequirePermission(PermissionResource.Plugins);

        group.MapGet("/", (PluginEngine.PluginEngine engine) =>
        {
            var registrations = engine.Registry.GetAll();
            var response = registrations.Select(r => new LoadedPluginResponse(
                r.Metadata.PluginId,
                r.Metadata.Name,
                r.Metadata.Version.ToString(),
                r.Metadata.InformationalVersion,
                r.Metadata.Description,
                r.Metadata.Kind.ToString(),
                r.Metadata.ProbeMode?.ToString(),
                r.Metadata.ConfigManifest)).ToList();

            return Results.Ok(response);
        });
    }
}
