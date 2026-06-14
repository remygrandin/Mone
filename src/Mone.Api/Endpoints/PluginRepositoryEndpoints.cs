using Microsoft.EntityFrameworkCore;
using Mone.Api.Models;
using Mone.Infrastructure.Data;
using Mone.Infrastructure.Data.Entities;
using Mone.Infrastructure.Services;
using Mone.Messaging;
using Mone.Messaging.Messages;
using NATS.Client.Core;

using Mone.Api.Authorization;
using Mone.Contracts.Models;

namespace Mone.Api.Endpoints;

public static class PluginRepositoryEndpoints
{
    public static void MapPluginRepositoryEndpoints(this WebApplication app)
    {
        var repos = app.MapGroup("/api/plugin-repos")
            .WithTags("Plugins")
            .RequireAuthorization()
            .RequirePermission(PermissionResource.Plugins);
        var plugins = app.MapGroup("/api/plugins")
            .WithTags("Plugins")
            .RequireAuthorization()
            .RequirePermission(PermissionResource.Plugins);

        repos.MapPost("/", async (AddRepositoryRequest request, MoneDbContext db, IServiceScopeFactory scopeFactory, ILoggerFactory loggerFactory) =>
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

            var repositoryId = entity.Id;
            _ = Task.Run(async () =>
            {
                using var scope = scopeFactory.CreateScope();
                var svc = scope.ServiceProvider.GetRequiredService<IPluginRepositoryService>();
                try { await svc.SyncRepositoryAsync(repositoryId); }
                catch (Exception ex)
                {
                    loggerFactory.CreateLogger("Mone.Api.PluginRepository")
                        .LogWarning(ex, "Background sync failed for repository {RepositoryId}", repositoryId);
                }
            });

            return Results.Created($"/api/plugin-repos/{entity.Id}", ToResponse(entity));
        })
        .WithName("AddPluginRepository")
        .WithSummary("Register a plugin repository and trigger an initial background sync.")
        .Produces<PluginRepositoryResponse>(StatusCodes.Status201Created);

        repos.MapGet("/", async (MoneDbContext db) =>
        {
            var list = await db.PluginRepositories
                .OrderBy(r => r.DisplayName)
                .Select(r => new PluginRepositoryResponse(
                    r.Id, r.Owner, r.Repo, r.Branch, r.DisplayName,
                    r.Enabled, r.LastSyncedAt, r.LastSyncError, r.CreatedAt))
                .ToListAsync();

            return Results.Ok(list);
        })
        .WithName("ListPluginRepositories")
        .WithSummary("List all registered plugin repositories.")
        .Produces<IEnumerable<PluginRepositoryResponse>>();

        repos.MapGet("/{id:guid}", async (Guid id, MoneDbContext db) =>
        {
            var repo = await db.PluginRepositories
                .Where(r => r.Id == id)
                .Select(r => new PluginRepositoryResponse(
                    r.Id, r.Owner, r.Repo, r.Branch, r.DisplayName,
                    r.Enabled, r.LastSyncedAt, r.LastSyncError, r.CreatedAt))
                .FirstOrDefaultAsync();

            return repo is null
                ? Results.Problem("Plugin repository not found.", statusCode: StatusCodes.Status404NotFound)
                : Results.Ok(repo);
        })
        .WithName("GetPluginRepository")
        .WithSummary("Get a single plugin repository by id. Returns 404 if not found.")
        .Produces<PluginRepositoryResponse>()
        .ProducesProblem(StatusCodes.Status404NotFound);

        repos.MapDelete("/{id:guid}", async (Guid id, MoneDbContext db) =>
        {
            var repo = await db.PluginRepositories
                .Include(r => r.Manifests)
                .FirstOrDefaultAsync(r => r.Id == id);

            if (repo is null) return Results.Problem("Plugin repository not found.", statusCode: StatusCodes.Status404NotFound);

            db.PluginRepositories.Remove(repo);
            await db.SaveChangesAsync();
            return Results.NoContent();
        })
        .WithName("DeletePluginRepository")
        .WithSummary("Delete a plugin repository and its manifests. Returns 404 if not found.")
        .Produces(StatusCodes.Status204NoContent)
        .ProducesProblem(StatusCodes.Status404NotFound);

        repos.MapPost("/{id:guid}/sync", async (Guid id, MoneDbContext db, IPluginRepositoryService svc) =>
        {
            var exists = await db.PluginRepositories.AnyAsync(r => r.Id == id);
            if (!exists) return Results.Problem("Plugin repository not found.", statusCode: StatusCodes.Status404NotFound);

            await svc.SyncRepositoryAsync(id);
            return Results.Ok();
        })
        .WithName("SyncPluginRepository")
        .WithSummary("Sync a single plugin repository by id. Returns 404 if not found.")
        .Produces(StatusCodes.Status200OK)
        .ProducesProblem(StatusCodes.Status404NotFound);

        repos.MapPost("/sync-all", async (MoneDbContext db, IPluginRepositoryService svc) =>
        {
            var ids = await db.PluginRepositories
                .Where(r => r.Enabled)
                .Select(r => r.Id)
                .ToListAsync();

            foreach (var id in ids)
                await svc.SyncRepositoryAsync(id);

            return Results.Ok(new RepositorySyncAllResponse(ids.Count));
        })
        .WithName("SyncAllPluginRepositories")
        .WithSummary("Sync every enabled plugin repository and report how many were synced.")
        .Produces<RepositorySyncAllResponse>();

        plugins.MapGet("/", async (IPluginRepositoryService svc, IInstalledPluginQuery installed) =>
        {
            var manifests = await svc.GetAvailablePluginsAsync();
            return Results.Ok(BuildCatalog(manifests, installed.List()));
        })
        .WithName("ListPluginCatalog")
        .WithSummary("List the plugin catalog (available versions merged with installed state).")
        .Produces<IEnumerable<PluginCatalogResponse>>();

        plugins.MapPost("/install", async (
            InstallPluginRequest request,
            IPluginRepositoryService svc,
            Mone.PluginEngine.PluginEngine engine,
            INatsConnection nats,
            MoneDbContext db,
            IConfiguration config,
            ILoggerFactory loggerFactory) =>
        {
            await svc.InstallPluginAsync(request.VersionId);

            var name = await db.PluginManifests
                .Where(m => m.Id == request.VersionId)
                .Select(m => m.Name)
                .FirstOrDefaultAsync();

            ReloadEngineFromDisk(engine, config);

            if (!string.IsNullOrEmpty(name))
                await PublishReloadAsync(nats, loggerFactory, PluginReloadAction.Install, name);

            return Results.Ok();
        })
        .WithName("InstallPlugin")
        .WithSummary("Install a plugin version, reload the engine from disk, and broadcast a reload.")
        .Produces(StatusCodes.Status200OK);

        plugins.MapPost("/uninstall", async (
            UninstallPluginRequest request,
            IPluginRepositoryService svc,
            Mone.PluginEngine.PluginEngine engine,
            INatsConnection nats,
            IConfiguration config,
            ILoggerFactory loggerFactory) =>
        {
            UnloadEngineForPlugin(engine, config, request.Name);
            await svc.UninstallPluginAsync(request.Name);
            await PublishReloadAsync(nats, loggerFactory, PluginReloadAction.Uninstall, request.Name);
            return Results.Ok();
        })
        .WithName("UninstallPlugin")
        .WithSummary("Unload and uninstall a plugin by name, then broadcast a reload.")
        .Produces(StatusCodes.Status200OK);

        plugins.MapPost("/reload", async (
            Mone.PluginEngine.PluginEngine engine,
            IConfiguration config,
            INatsConnection nats,
            ILoggerFactory loggerFactory) =>
        {
            EvictMissingFromEngine(engine, loggerFactory);
            ReloadEngineFromDisk(engine, config);
            await PublishReloadAsync(nats, loggerFactory, PluginReloadAction.ReloadAll, string.Empty);
            return Results.Ok();
        })
        .WithName("ReloadPlugins")
        .WithSummary("Evict missing assemblies, reload all plugins from disk, and broadcast a reload.")
        .Produces(StatusCodes.Status200OK);
    }

    private static List<PluginCatalogResponse> BuildCatalog(
        IReadOnlyList<PluginManifestEntity> manifests,
        IReadOnlyList<InstalledPluginInfo> installed)
    {
        var installedByName = installed.ToDictionary(p => p.Name, StringComparer.OrdinalIgnoreCase);

        var grouped = manifests
            .GroupBy(m => m.Name, StringComparer.OrdinalIgnoreCase);

        var catalog = new List<PluginCatalogResponse>();

        foreach (var group in grouped)
        {
            var sortedVersions = group
                .OrderByDescending(m => m, ManifestVersionComparer.Instance)
                .ToList();

            var latest = sortedVersions[0];
            var latestStable = sortedVersions.FirstOrDefault(m => !m.IsPrerelease);
            installedByName.TryGetValue(group.Key, out var onDisk);

            // Status uses latest stable for "update available" — pre-releases
            // shouldn't pop the badge by default. If no stable exists, fall
            // back to the absolute latest so users on a prerelease still see
            // newer prereleases.
            var referenceVersion = latestStable?.Version ?? latest.Version;
            var status = ComputeStatus(referenceVersion, onDisk?.Version);

            catalog.Add(new PluginCatalogResponse(
                Name: group.Key,
                Description: latest.Description,
                PluginType: latest.PluginType,
                Author: latest.Author,
                License: latest.License,
                Homepage: latest.Homepage,
                Status: status,
                RepositoryId: latest.RepositoryId,
                LatestVersion: latest.Version,
                LatestStableVersion: latestStable?.Version,
                InstalledVersion: onDisk?.Version,
                SyncedAt: latest.SyncedAt,
                Versions: sortedVersions.Select(v => new PluginVersionResponse(
                    Id: v.Id,
                    Version: v.Version,
                    ReleaseTag: v.ReleaseTag,
                    PublishedAt: v.PublishedAt,
                    IsPrerelease: v.IsPrerelease,
                    Sha256: v.Sha256,
                    FileSize: v.FileSize)).ToList()));
        }

        foreach (var (name, onDisk) in installedByName)
        {
            if (catalog.Any(c => string.Equals(c.Name, name, StringComparison.OrdinalIgnoreCase))) continue;

            catalog.Add(new PluginCatalogResponse(
                Name: name,
                Description: null,
                PluginType: "Unknown",
                Author: null,
                License: null,
                Homepage: null,
                Status: PluginStatus.Installed,
                RepositoryId: null,
                LatestVersion: null,
                LatestStableVersion: null,
                InstalledVersion: onDisk.Version,
                SyncedAt: null,
                Versions: []));
        }

        return [.. catalog.OrderBy(c => c.Name, StringComparer.OrdinalIgnoreCase)];
    }

    private static PluginStatus ComputeStatus(string latestVersion, string? installedVersion)
    {
        if (installedVersion is null) return PluginStatus.Available;
        var cmp = SemverCompare(installedVersion, latestVersion);
        return cmp < 0 ? PluginStatus.UpdateAvailable : PluginStatus.Installed;
    }

    private static async Task PublishReloadAsync(
        INatsConnection nats,
        ILoggerFactory loggerFactory,
        PluginReloadAction action,
        string pluginName)
    {
        var logger = loggerFactory.CreateLogger("Mone.Api.PluginReload");
        try
        {
            await nats.PublishAsync(
                MoneStreams.PluginReload.Subject,
                new PluginReloadMessage(action, pluginName));
            logger.LogInformation(
                "Published PluginReload — Action={Action}, PluginName={PluginName}",
                action, pluginName);
        }
        catch (Exception ex)
        {
            logger.LogWarning(ex,
                "Failed to publish PluginReload — Action={Action}, PluginName={PluginName}",
                action, pluginName);
        }
    }

    private static string GetPluginDir(IConfiguration config) =>
        Path.GetFullPath(config["Api:PluginDirectory"] ?? "plugins");

    private static void ReloadEngineFromDisk(Mone.PluginEngine.PluginEngine engine, IConfiguration config)
    {
        var dir = GetPluginDir(config);
        Directory.CreateDirectory(dir);
        engine.LoadPluginsFromDirectory(dir);
    }

    private static void UnloadEngineForPlugin(Mone.PluginEngine.PluginEngine engine, IConfiguration config, string name)
    {
        var pluginDir = Path.GetFullPath(Path.Combine(GetPluginDir(config), name));
        var prefix = pluginDir + Path.DirectorySeparatorChar;
        var toUnload = engine.LoadedAssemblyPaths
            .Where(p => p.StartsWith(prefix, StringComparison.OrdinalIgnoreCase))
            .ToList();
        foreach (var dll in toUnload)
            engine.UnloadPlugin(dll);
    }

    private static void EvictMissingFromEngine(Mone.PluginEngine.PluginEngine engine, ILoggerFactory loggerFactory)
    {
        var logger = loggerFactory.CreateLogger("Mone.Api.PluginReload");
        var missing = engine.LoadedAssemblyPaths
            .Where(p => !File.Exists(p))
            .ToList();
        foreach (var dll in missing)
        {
            logger.LogInformation("Evicting plugin assembly no longer on disk: {Path}", dll);
            engine.UnloadPlugin(dll);
        }
    }

    private static PluginRepositoryResponse ToResponse(PluginRepositoryEntity e) =>
        new(e.Id, e.Owner, e.Repo, e.Branch, e.DisplayName,
            e.Enabled, e.LastSyncedAt, e.LastSyncError, e.CreatedAt);

    private static int SemverCompare(string a, string b)
    {
        if (string.Equals(a, b, StringComparison.OrdinalIgnoreCase)) return 0;

        var (coreA, preA) = SplitVersion(a);
        var (coreB, preB) = SplitVersion(b);

        var coreCmp = CompareCore(coreA, coreB);
        if (coreCmp != 0) return coreCmp;

        if (string.IsNullOrEmpty(preA) && string.IsNullOrEmpty(preB)) return 0;
        if (string.IsNullOrEmpty(preA)) return 1;
        if (string.IsNullOrEmpty(preB)) return -1;
        return string.Compare(preA, preB, StringComparison.OrdinalIgnoreCase);
    }

    private static (string core, string prerelease) SplitVersion(string v)
    {
        var plus = v.IndexOf('+');
        if (plus >= 0) v = v[..plus];
        var dash = v.IndexOf('-');
        return dash < 0 ? (v, string.Empty) : (v[..dash], v[(dash + 1)..]);
    }

    private static int CompareCore(string a, string b)
    {
        var pa = a.Split('.');
        var pb = b.Split('.');
        var n = Math.Max(pa.Length, pb.Length);
        for (var i = 0; i < n; i++)
        {
            var xa = i < pa.Length && int.TryParse(pa[i], out var ia) ? ia : 0;
            var xb = i < pb.Length && int.TryParse(pb[i], out var ib) ? ib : 0;
            if (xa != xb) return xa.CompareTo(xb);
        }
        return 0;
    }

    private sealed class ManifestVersionComparer : IComparer<PluginManifestEntity>
    {
        public static readonly ManifestVersionComparer Instance = new();
        public int Compare(PluginManifestEntity? x, PluginManifestEntity? y)
        {
            if (x is null && y is null) return 0;
            if (x is null) return -1;
            if (y is null) return 1;
            return SemverCompare(x.Version, y.Version);
        }
    }
}
