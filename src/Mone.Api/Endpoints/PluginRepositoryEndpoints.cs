using Microsoft.EntityFrameworkCore;
using Mone.Api.Models;
using Mone.Infrastructure.Data;
using Mone.Infrastructure.Data.Entities;
using Mone.Infrastructure.Services;

namespace Mone.Api.Endpoints;

public static class PluginRepositoryEndpoints
{
    public static void MapPluginRepositoryEndpoints(this WebApplication app)
    {
        var repos = app.MapGroup("/api/plugin-repos").RequireAuthorization();
        var plugins = app.MapGroup("/api/plugins").RequireAuthorization();

        repos.MapPost("/", async (AddRepositoryRequest request, MoneDbContext db, IPluginRepositoryService svc) =>
        {
            var entity = new PluginRepositoryEntity
            {
                Id = Guid.NewGuid(),
                Owner = request.Owner,
                Repo = request.Repo,
                Branch = request.Branch,
                DisplayName = request.DisplayName ?? $"{request.Owner}/{request.Repo}",
                CreatedAt = DateTime.UtcNow
            };

            db.PluginRepositories.Add(entity);
            await db.SaveChangesAsync();

            _ = Task.Run(() => svc.SyncRepositoryAsync(entity.Id));

            return Results.Created($"/api/plugin-repos/{entity.Id}", ToResponse(entity));
        });

        repos.MapGet("/", async (MoneDbContext db) =>
        {
            var list = await db.PluginRepositories
                .OrderBy(r => r.DisplayName)
                .Select(r => new PluginRepositoryResponse(
                    r.Id, r.Owner, r.Repo, r.Branch, r.DisplayName,
                    r.Enabled, r.LastSyncedAt, r.LastSyncError, r.CreatedAt))
                .ToListAsync();

            return Results.Ok(list);
        });

        repos.MapGet("/{id:guid}", async (Guid id, MoneDbContext db) =>
        {
            var repo = await db.PluginRepositories
                .Where(r => r.Id == id)
                .Select(r => new PluginRepositoryResponse(
                    r.Id, r.Owner, r.Repo, r.Branch, r.DisplayName,
                    r.Enabled, r.LastSyncedAt, r.LastSyncError, r.CreatedAt))
                .FirstOrDefaultAsync();

            return repo is null ? Results.NotFound() : Results.Ok(repo);
        });

        repos.MapDelete("/{id:guid}", async (Guid id, MoneDbContext db) =>
        {
            var repo = await db.PluginRepositories
                .Include(r => r.Manifests)
                .FirstOrDefaultAsync(r => r.Id == id);

            if (repo is null) return Results.NotFound();

            db.PluginRepositories.Remove(repo);
            await db.SaveChangesAsync();
            return Results.NoContent();
        });

        repos.MapPost("/{id:guid}/sync", async (Guid id, MoneDbContext db, IPluginRepositoryService svc) =>
        {
            var exists = await db.PluginRepositories.AnyAsync(r => r.Id == id);
            if (!exists) return Results.NotFound();

            await svc.SyncRepositoryAsync(id);
            return Results.Ok();
        });

        plugins.MapGet("/", async (IPluginRepositoryService svc) =>
        {
            var all = await svc.GetAvailablePluginsAsync();
            var response = all.Select(ToManifestResponse).ToList();
            return Results.Ok(response);
        });

        plugins.MapPost("/install", async (InstallPluginRequest request, IPluginRepositoryService svc) =>
        {
            await svc.InstallPluginAsync(request.ManifestId);
            return Results.Ok();
        });

        plugins.MapPost("/uninstall", async (InstallPluginRequest request, IPluginRepositoryService svc) =>
        {
            await svc.UninstallPluginAsync(request.ManifestId);
            return Results.Ok();
        });
    }

    private static PluginRepositoryResponse ToResponse(PluginRepositoryEntity e) =>
        new(e.Id, e.Owner, e.Repo, e.Branch, e.DisplayName,
            e.Enabled, e.LastSyncedAt, e.LastSyncError, e.CreatedAt);

    private static PluginManifestResponse ToManifestResponse(PluginManifestEntity e) =>
        new(e.Id, e.RepositoryId, e.Name, e.Version, e.Description,
            e.PluginType, e.Author, e.License, e.Homepage,
            e.IsInstalled, e.InstalledAt, e.SyncedAt);
}
