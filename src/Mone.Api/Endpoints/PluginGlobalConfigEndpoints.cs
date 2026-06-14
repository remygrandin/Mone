using Microsoft.EntityFrameworkCore;
using Mone.Infrastructure.Data;
using Mone.Infrastructure.Data.Entities;

using Mone.Api.Authorization;
using Mone.Contracts.Models;

namespace Mone.Api.Endpoints;

public static class PluginGlobalConfigEndpoints
{
    public record PluginGlobalConfigResponse(string PluginId, string? ConfigJson, DateTime UpdatedAt);
    public record UpsertPluginGlobalConfigRequest(string? ConfigJson);

    public static void MapPluginGlobalConfigEndpoints(this WebApplication app)
    {
        var group = app.MapGroup("/api/plugins/{pluginId}/global-config")
            .WithTags("Plugins")
            .RequireAuthorization()
            .RequirePermission(PermissionResource.Plugins);

        group.MapGet("/", async (string pluginId, MoneDbContext db) =>
        {
            var config = await db.PluginGlobalConfigs
                .FirstOrDefaultAsync(c => c.PluginId == pluginId);

            if (config is null)
                return Results.Ok(new PluginGlobalConfigResponse(pluginId, "{}", DateTime.UtcNow));

            return Results.Ok(new PluginGlobalConfigResponse(config.PluginId, config.ConfigJson, config.UpdatedAt));
        })
        .WithName("GetPluginGlobalConfig")
        .WithSummary("Get a plugin's global config, synthesizing an empty default when none is stored.")
        .Produces<PluginGlobalConfigResponse>();

        group.MapPut("/", async (string pluginId, UpsertPluginGlobalConfigRequest request, MoneDbContext db) =>
        {
            var config = await db.PluginGlobalConfigs
                .FirstOrDefaultAsync(c => c.PluginId == pluginId);

            if (config is null)
            {
                config = new PluginGlobalConfigEntity
                {
                    Id = Guid.NewGuid(),
                    PluginId = pluginId,
                    ConfigJson = request.ConfigJson,
                    UpdatedAt = DateTime.UtcNow
                };
                db.PluginGlobalConfigs.Add(config);
            }
            else
            {
                config.ConfigJson = request.ConfigJson;
                config.UpdatedAt = DateTime.UtcNow;
            }

            await db.SaveChangesAsync();

            return Results.Ok(new PluginGlobalConfigResponse(config.PluginId, config.ConfigJson, config.UpdatedAt));
        })
        .WithName("UpsertPluginGlobalConfig")
        .WithSummary("Create or replace a plugin's global config JSON.")
        .Produces<PluginGlobalConfigResponse>();
    }
}
