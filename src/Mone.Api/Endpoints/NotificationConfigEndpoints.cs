using Microsoft.EntityFrameworkCore;
using Mone.Api.Models;
using Mone.Infrastructure.Data;
using Mone.Infrastructure.Data.Entities;

using Mone.Api.Authorization;
using Mone.Contracts.Models;

namespace Mone.Api.Endpoints;

public static class NotificationConfigEndpoints
{
    public static void MapNotificationConfigEndpoints(this WebApplication app)
    {
        var group = app.MapGroup("/api/notifications/configs")
            .RequireAuthorization()
            .RequirePermission(PermissionResource.Notifications);

        group.MapGet("/", async (MoneDbContext db) =>
        {
            var configs = await db.NotificationConfigs
                .Select(c => new NotificationConfigResponse(c.Id, c.PluginId, c.ConfigJson, c.Enabled, c.Scope))
                .ToListAsync();

            return Results.Ok(configs);
        });

        group.MapGet("/{id:guid}", async (Guid id, MoneDbContext db) =>
        {
            var config = await db.NotificationConfigs
                .Where(c => c.Id == id)
                .Select(c => new NotificationConfigResponse(c.Id, c.PluginId, c.ConfigJson, c.Enabled, c.Scope))
                .FirstOrDefaultAsync();

            return config is null ? Results.NotFound() : Results.Ok(config);
        });

        group.MapPost("/", async (CreateNotificationConfigRequest request, MoneDbContext db) =>
        {
            var config = new NotificationConfigEntity
            {
                Id = Guid.NewGuid(),
                PluginId = request.PluginId,
                ConfigJson = request.ConfigJson,
                Enabled = request.Enabled,
                Scope = request.Scope
            };

            db.NotificationConfigs.Add(config);
            await db.SaveChangesAsync();

            var response = new NotificationConfigResponse(config.Id, config.PluginId, config.ConfigJson, config.Enabled, config.Scope);
            return Results.Created($"/api/notifications/configs/{config.Id}", response);
        });

        group.MapPut("/{id:guid}", async (Guid id, UpdateNotificationConfigRequest request, MoneDbContext db) =>
        {
            var config = await db.NotificationConfigs.FirstOrDefaultAsync(c => c.Id == id);

            if (config is null) return Results.NotFound();

            config.PluginId = request.PluginId;
            config.ConfigJson = request.ConfigJson;
            config.Enabled = request.Enabled;
            config.Scope = request.Scope;

            await db.SaveChangesAsync();

            var response = new NotificationConfigResponse(config.Id, config.PluginId, config.ConfigJson, config.Enabled, config.Scope);
            return Results.Ok(response);
        });

        group.MapDelete("/{id:guid}", async (Guid id, MoneDbContext db) =>
        {
            var config = await db.NotificationConfigs.FirstOrDefaultAsync(c => c.Id == id);

            if (config is null) return Results.NotFound();

            db.NotificationConfigs.Remove(config);
            await db.SaveChangesAsync();
            return Results.NoContent();
        });
    }
}
