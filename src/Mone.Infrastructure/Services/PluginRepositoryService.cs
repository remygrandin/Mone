using System.IO.Compression;
using System.Net;
using System.Net.Http.Headers;
using System.Security.Cryptography;
using System.Text.Json;
using System.Text.Json.Serialization;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using Mone.Infrastructure.Data;
using Mone.Infrastructure.Data.Entities;

namespace Mone.Infrastructure.Services;

public sealed class PluginRepositoryService : IPluginRepositoryService
{
    private readonly MoneDbContext _db;
    private readonly IHttpClientFactory _httpClientFactory;
    private readonly ILogger<PluginRepositoryService> _logger;
    private readonly string _pluginsBaseDir;

    private static readonly JsonSerializerOptions ManifestJsonOptions = new()
    {
        PropertyNameCaseInsensitive = true,
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull
    };

    public PluginRepositoryService(
        MoneDbContext db,
        IHttpClientFactory httpClientFactory,
        ILogger<PluginRepositoryService> logger,
        string? pluginsBaseDir = null)
    {
        _db = db;
        _httpClientFactory = httpClientFactory;
        _logger = logger;
        _pluginsBaseDir = pluginsBaseDir ?? Path.Combine(AppContext.BaseDirectory, "plugins");
    }

    private const int MaxReleasesPerSync = 10;
    private const string ManifestAssetName = "mone-plugins.json";

    public async Task SyncRepositoryAsync(Guid repoId, CancellationToken ct = default)
    {
        var sw = System.Diagnostics.Stopwatch.StartNew();
        _logger.LogInformation("SyncRepository started — RepositoryId={RepositoryId}", repoId);

        var repo = await _db.PluginRepositories
            .FirstOrDefaultAsync(r => r.Id == repoId, ct);

        if (repo is null)
        {
            _logger.LogWarning("SyncRepository skipped — RepositoryId={RepositoryId} not found", repoId);
            return;
        }

        if (!repo.Enabled)
        {
            _logger.LogInformation("SyncRepository skipped — RepositoryId={RepositoryId} is disabled", repoId);
            return;
        }

        var client = _httpClientFactory.CreateClient("GitHub");

        try
        {
            var releasesUrl = $"https://api.github.com/repos/{repo.Owner}/{repo.Repo}/releases?per_page={MaxReleasesPerSync}";
            using var releasesRequest = new HttpRequestMessage(HttpMethod.Get, releasesUrl);
            releasesRequest.Headers.UserAgent.Add(new ProductInfoHeaderValue("Mone", "1.0"));
            releasesRequest.Headers.Accept.Add(new MediaTypeWithQualityHeaderValue("application/vnd.github.v3+json"));

            if (!string.IsNullOrEmpty(repo.ETag))
                releasesRequest.Headers.IfNoneMatch.Add(new EntityTagHeaderValue(repo.ETag));

            using var releasesResponse = await client.SendAsync(releasesRequest, ct);

            if (releasesResponse.StatusCode == HttpStatusCode.NotModified)
            {
                repo.LastSyncedAt = DateTime.UtcNow;
                repo.LastSyncError = null;
                await _db.SaveChangesAsync(ct);

                sw.Stop();
                _logger.LogInformation(
                    "SyncRepository complete (not modified) — RepositoryId={RepositoryId}, Duration={Duration}ms",
                    repoId, sw.ElapsedMilliseconds);
                return;
            }

            if (releasesResponse.StatusCode == HttpStatusCode.NotFound)
            {
                repo.LastSyncError = "Repository not found (404)";
                repo.LastSyncedAt = DateTime.UtcNow;
                await _db.SaveChangesAsync(ct);

                sw.Stop();
                _logger.LogWarning(
                    "SyncRepository failed — RepositoryId={RepositoryId}, Error=NotFound, Duration={Duration}ms",
                    repoId, sw.ElapsedMilliseconds);
                return;
            }

            if (releasesResponse.StatusCode == HttpStatusCode.Forbidden ||
                releasesResponse.StatusCode == (HttpStatusCode)429)
            {
                repo.LastSyncError = $"Rate limited or forbidden ({(int)releasesResponse.StatusCode})";
                repo.LastSyncedAt = DateTime.UtcNow;
                await _db.SaveChangesAsync(ct);

                sw.Stop();
                _logger.LogWarning(
                    "SyncRepository rate-limited — RepositoryId={RepositoryId}, StatusCode={StatusCode}, Duration={Duration}ms",
                    repoId, (int)releasesResponse.StatusCode, sw.ElapsedMilliseconds);
                return;
            }

            releasesResponse.EnsureSuccessStatusCode();

            var releasesJson = await releasesResponse.Content.ReadAsStringAsync(ct);
            var releases = JsonSerializer.Deserialize<List<GitHubRelease>>(releasesJson, ManifestJsonOptions)
                           ?? [];

            if (releases.Count == 0)
            {
                repo.ETag = releasesResponse.Headers.ETag?.Tag;
                repo.LastSyncError = "Repository has no releases";
                repo.LastSyncedAt = DateTime.UtcNow;
                await _db.SaveChangesAsync(ct);

                sw.Stop();
                _logger.LogWarning(
                    "SyncRepository — RepositoryId={RepositoryId}, no releases found", repoId);
                return;
            }

            var existingVersions = await _db.PluginManifests
                .Where(m => m.RepositoryId == repoId)
                .ToListAsync(ct);

            var existingLookup = existingVersions
                .ToDictionary(m => (m.Name, m.Version));

            var existingReleaseTags = existingVersions
                .Select(m => m.ReleaseTag)
                .ToHashSet(StringComparer.OrdinalIgnoreCase);

            var now = DateTime.UtcNow;
            var totalPlugins = 0;
            var skippedReleases = 0;

            foreach (var release in releases)
            {
                if (release.Draft) continue;

                if (release.TagName is null)
                {
                    skippedReleases++;
                    continue;
                }

                // Releases are immutable once tagged with build-N; if we've ingested
                // this tag before, every (name, version) in it is already in DB.
                if (existingReleaseTags.Contains(release.TagName)) continue;

                var manifestAsset = release.Assets?
                    .FirstOrDefault(a => string.Equals(a.Name, ManifestAssetName, StringComparison.OrdinalIgnoreCase));

                if (manifestAsset?.BrowserDownloadUrl is null)
                {
                    skippedReleases++;
                    _logger.LogDebug(
                        "Release {Tag} on RepositoryId={RepositoryId} has no {Asset}, skipping",
                        release.TagName, repoId, ManifestAssetName);
                    continue;
                }

                var manifest = await FetchReleaseManifestAsync(client, manifestAsset.BrowserDownloadUrl, ct);
                if (manifest?.Plugins is null || manifest.Plugins.Count == 0)
                {
                    skippedReleases++;
                    continue;
                }

                var publishedAt = release.PublishedAt ?? release.CreatedAt ?? now;

                foreach (var plugin in manifest.Plugins)
                {
                    UpsertVersion(repoId, release.TagName, publishedAt, release.Prerelease,
                        plugin, existingLookup, now);
                    totalPlugins++;
                }
            }

            repo.ETag = releasesResponse.Headers.ETag?.Tag;
            repo.LastSyncedAt = now;
            repo.LastSyncError = null;

            await _db.SaveChangesAsync(ct);

            sw.Stop();
            _logger.LogInformation(
                "SyncRepository complete — RepositoryId={RepositoryId}, ReleasesScanned={ReleasesScanned}, " +
                "ReleasesSkipped={ReleasesSkipped}, PluginsIngested={PluginsIngested}, Duration={Duration}ms",
                repoId, releases.Count, skippedReleases, totalPlugins, sw.ElapsedMilliseconds);
        }
        catch (Exception ex)
        {
            sw.Stop();
            repo.LastSyncError = ex.Message;
            repo.LastSyncedAt = DateTime.UtcNow;
            await _db.SaveChangesAsync(ct);

            _logger.LogError(ex,
                "SyncRepository error — RepositoryId={RepositoryId}, Duration={Duration}ms",
                repoId, sw.ElapsedMilliseconds);
            throw;
        }
    }

    private async Task<PluginManifestFile?> FetchReleaseManifestAsync(
        HttpClient client, string assetUrl, CancellationToken ct)
    {
        using var request = new HttpRequestMessage(HttpMethod.Get, assetUrl);
        request.Headers.UserAgent.Add(new ProductInfoHeaderValue("Mone", "1.0"));

        using var response = await client.SendAsync(request, ct);
        if (!response.IsSuccessStatusCode)
        {
            _logger.LogWarning(
                "Failed to fetch release manifest from {Url}: {StatusCode}",
                assetUrl, response.StatusCode);
            return null;
        }

        var json = await response.Content.ReadAsStringAsync(ct);
        return JsonSerializer.Deserialize<PluginManifestFile>(json, ManifestJsonOptions);
    }

    private void UpsertVersion(
        Guid repoId,
        string releaseTag,
        DateTime publishedAt,
        bool isPrerelease,
        PluginEntry plugin,
        Dictionary<(string, string), PluginManifestEntity> existingLookup,
        DateTime now)
    {
        if (existingLookup.TryGetValue((plugin.Name, plugin.Version), out var existing))
        {
            existing.Description = plugin.Description;
            existing.PluginType = plugin.PluginType;
            existing.DownloadUrl = plugin.DownloadUrl;
            existing.Sha256 = plugin.Sha256;
            existing.FileSize = plugin.FileSize;
            existing.DependenciesJson = plugin.Dependencies is not null
                ? JsonSerializer.Serialize(plugin.Dependencies, ManifestJsonOptions)
                : null;
            existing.MinMoneVersion = plugin.MinMoneVersion;
            existing.Author = plugin.Author;
            existing.License = plugin.License;
            existing.Homepage = plugin.Homepage;
            existing.TagsJson = plugin.Tags is not null
                ? JsonSerializer.Serialize(plugin.Tags, ManifestJsonOptions)
                : null;
            existing.SyncedAt = now;
            existing.ReleaseTag = releaseTag;
            existing.PublishedAt = publishedAt;
            existing.IsPrerelease = isPrerelease;
            return;
        }

        _db.PluginManifests.Add(new PluginManifestEntity
        {
            Id = Guid.NewGuid(),
            RepositoryId = repoId,
            Name = plugin.Name,
            Version = plugin.Version,
            Description = plugin.Description,
            PluginType = plugin.PluginType,
            DownloadUrl = plugin.DownloadUrl,
            Sha256 = plugin.Sha256,
            FileSize = plugin.FileSize,
            DependenciesJson = plugin.Dependencies is not null
                ? JsonSerializer.Serialize(plugin.Dependencies, ManifestJsonOptions)
                : null,
            MinMoneVersion = plugin.MinMoneVersion,
            Author = plugin.Author,
            License = plugin.License,
            Homepage = plugin.Homepage,
            TagsJson = plugin.Tags is not null
                ? JsonSerializer.Serialize(plugin.Tags, ManifestJsonOptions)
                : null,
            SyncedAt = now,
            ReleaseTag = releaseTag,
            PublishedAt = publishedAt,
            IsPrerelease = isPrerelease
        });
    }

    public async Task InstallPluginAsync(Guid manifestId, CancellationToken ct = default)
    {
        _logger.LogInformation("InstallPlugin started — ManifestId={ManifestId}", manifestId);

        var manifest = await _db.PluginManifests
            .AsNoTracking()
            .Include(m => m.Repository)
            .FirstOrDefaultAsync(m => m.Id == manifestId, ct)
            ?? throw new InvalidOperationException($"Plugin manifest {manifestId} not found");

        var client = _httpClientFactory.CreateClient("GitHub");
        using var request = new HttpRequestMessage(HttpMethod.Get, manifest.DownloadUrl);
        request.Headers.UserAgent.Add(new ProductInfoHeaderValue("Mone", "1.0"));

        using var response = await client.SendAsync(request, ct);
        response.EnsureSuccessStatusCode();

        var zipBytes = await response.Content.ReadAsByteArrayAsync(ct);

        var actualHash = Convert.ToHexStringLower(SHA256.HashData(zipBytes));
        var expectedHash = manifest.Sha256.ToLowerInvariant();

        if (actualHash != expectedHash)
        {
            _logger.LogError(
                "InstallPlugin SHA256 mismatch — ManifestId={ManifestId}, PluginName={PluginName}, Expected={Expected}, Actual={Actual}",
                manifestId, manifest.Name, expectedHash, actualHash);
            throw new InvalidOperationException(
                $"SHA256 hash mismatch for plugin '{manifest.Name}': expected {expectedHash}, got {actualHash}");
        }

        Directory.CreateDirectory(_pluginsBaseDir);
        var pluginDir = Path.Combine(_pluginsBaseDir, manifest.Name);

        try
        {
            if (Directory.Exists(pluginDir))
                Directory.Delete(pluginDir, recursive: true);

            Directory.CreateDirectory(pluginDir);

            using var zipStream = new MemoryStream(zipBytes);
            ZipFile.ExtractToDirectory(zipStream, pluginDir);

            _logger.LogInformation(
                "InstallPlugin complete — ManifestId={ManifestId}, PluginName={PluginName}, Version={Version}, Path={InstalledPath}",
                manifestId, manifest.Name, manifest.Version, pluginDir);
        }
        catch (InvalidDataException ex)
        {
            if (Directory.Exists(pluginDir))
                Directory.Delete(pluginDir, recursive: true);

            _logger.LogError(ex,
                "InstallPlugin corrupt ZIP — ManifestId={ManifestId}, PluginName={PluginName}",
                manifestId, manifest.Name);
            throw new InvalidOperationException($"Corrupt ZIP archive for plugin '{manifest.Name}'", ex);
        }
    }

    public Task UninstallPluginAsync(string pluginName, CancellationToken ct = default)
    {
        _logger.LogInformation("UninstallPlugin started — PluginName={PluginName}", pluginName);

        if (string.IsNullOrWhiteSpace(pluginName) || pluginName.Contains('/') || pluginName.Contains('\\') || pluginName.Contains(".."))
            throw new InvalidOperationException($"Invalid plugin name: '{pluginName}'");

        var pluginDir = Path.Combine(_pluginsBaseDir, pluginName);
        if (!Directory.Exists(pluginDir))
        {
            _logger.LogInformation("UninstallPlugin skipped — PluginName={PluginName} not installed", pluginName);
            return Task.CompletedTask;
        }

        Directory.Delete(pluginDir, recursive: true);
        _logger.LogInformation("UninstallPlugin complete — PluginName={PluginName}, Path={InstalledPath}", pluginName, pluginDir);
        return Task.CompletedTask;
    }

    public async Task<IReadOnlyList<PluginManifestEntity>> GetAvailablePluginsAsync(CancellationToken ct = default)
    {
        var manifests = await _db.PluginManifests
            .Include(m => m.Repository)
            .Where(m => m.Repository.Enabled)
            .AsNoTracking()
            .ToListAsync(ct);

        return manifests
            .GroupBy(m => m.Name)
            .SelectMany(g => g
                .OrderByDescending(m =>
                {
                    var parsed = Version.TryParse(m.Version, out var v) ? v : new Version(0, 0, 0);
                    return parsed;
                })
                .ThenByDescending(m => m.PublishedAt))
            .ToList();
    }
}

internal sealed record GitHubRelease(
    [property: JsonPropertyName("tag_name")] string? TagName,
    [property: JsonPropertyName("name")] string? Name,
    [property: JsonPropertyName("draft")] bool Draft,
    [property: JsonPropertyName("prerelease")] bool Prerelease,
    [property: JsonPropertyName("created_at")] DateTime? CreatedAt,
    [property: JsonPropertyName("published_at")] DateTime? PublishedAt,
    [property: JsonPropertyName("assets")] IReadOnlyList<GitHubReleaseAsset>? Assets);

internal sealed record GitHubReleaseAsset(
    [property: JsonPropertyName("name")] string? Name,
    [property: JsonPropertyName("browser_download_url")] string? BrowserDownloadUrl,
    [property: JsonPropertyName("size")] long? Size);

public sealed record PluginManifestFile(
    [property: JsonPropertyName("plugins")] IReadOnlyList<PluginEntry> Plugins);

public sealed record PluginEntry(
    [property: JsonPropertyName("name")] string Name,
    [property: JsonPropertyName("version")] string Version,
    [property: JsonPropertyName("description")] string? Description,
    [property: JsonPropertyName("pluginType")] string PluginType,
    [property: JsonPropertyName("downloadUrl")] string DownloadUrl,
    [property: JsonPropertyName("sha256")] string Sha256,
    [property: JsonPropertyName("fileSize")] long? FileSize,
    [property: JsonPropertyName("dependencies")] IReadOnlyList<string>? Dependencies,
    [property: JsonPropertyName("minMoneVersion")] string? MinMoneVersion,
    [property: JsonPropertyName("author")] string? Author,
    [property: JsonPropertyName("license")] string? License,
    [property: JsonPropertyName("homepage")] string? Homepage,
    [property: JsonPropertyName("tags")] IReadOnlyList<string>? Tags);
