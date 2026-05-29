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

        try
        {
            var branch = repo.Branch ?? "main";
            var url = $"https://api.github.com/repos/{repo.Owner}/{repo.Repo}/contents/plugin-manifest.json?ref={branch}";

            var client = _httpClientFactory.CreateClient("GitHub");
            using var request = new HttpRequestMessage(HttpMethod.Get, url);
            request.Headers.UserAgent.Add(new ProductInfoHeaderValue("Mone", "1.0"));
            request.Headers.Accept.Add(new MediaTypeWithQualityHeaderValue("application/vnd.github.v3+json"));

            if (!string.IsNullOrEmpty(repo.ETag))
                request.Headers.IfNoneMatch.Add(new EntityTagHeaderValue(repo.ETag));

            using var response = await client.SendAsync(request, ct);

            if (response.StatusCode == HttpStatusCode.NotModified)
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

            if (response.StatusCode == HttpStatusCode.NotFound)
            {
                repo.LastSyncError = "Repository or manifest not found (404)";
                repo.LastSyncedAt = DateTime.UtcNow;
                await _db.SaveChangesAsync(ct);

                sw.Stop();
                _logger.LogWarning(
                    "SyncRepository failed — RepositoryId={RepositoryId}, Error=NotFound, Duration={Duration}ms",
                    repoId, sw.ElapsedMilliseconds);
                return;
            }

            if (response.StatusCode == HttpStatusCode.Forbidden ||
                response.StatusCode == (HttpStatusCode)429)
            {
                repo.LastSyncError = $"Rate limited or forbidden ({(int)response.StatusCode})";
                repo.LastSyncedAt = DateTime.UtcNow;
                await _db.SaveChangesAsync(ct);

                sw.Stop();
                _logger.LogWarning(
                    "SyncRepository rate-limited — RepositoryId={RepositoryId}, StatusCode={StatusCode}, Duration={Duration}ms",
                    repoId, (int)response.StatusCode, sw.ElapsedMilliseconds);
                return;
            }

            response.EnsureSuccessStatusCode();

            var contentJson = await response.Content.ReadAsStringAsync(ct);
            var ghContent = JsonSerializer.Deserialize<GitHubContentResponse>(contentJson, ManifestJsonOptions);

            if (ghContent?.Content is null)
            {
                repo.LastSyncError = "GitHub response missing content field";
                repo.LastSyncedAt = DateTime.UtcNow;
                await _db.SaveChangesAsync(ct);

                _logger.LogWarning(
                    "SyncRepository failed — RepositoryId={RepositoryId}, Error=MissingContent",
                    repoId);
                return;
            }

            var manifestBytes = Convert.FromBase64String(ghContent.Content.Replace("\n", ""));
            var manifestJson = System.Text.Encoding.UTF8.GetString(manifestBytes);
            var manifest = JsonSerializer.Deserialize<PluginManifestFile>(manifestJson, ManifestJsonOptions);

            if (manifest?.Plugins is null || manifest.Plugins.Count == 0)
            {
                repo.LastSyncError = "Manifest contains no plugins";
                repo.LastSyncedAt = DateTime.UtcNow;
                await _db.SaveChangesAsync(ct);

                _logger.LogWarning(
                    "SyncRepository failed — RepositoryId={RepositoryId}, Error=EmptyManifest",
                    repoId);
                return;
            }

            var existingManifests = await _db.PluginManifests
                .Where(m => m.RepositoryId == repoId)
                .ToListAsync(ct);

            var existingLookup = existingManifests
                .ToDictionary(m => (m.Name, m.Version));

            var now = DateTime.UtcNow;
            foreach (var plugin in manifest.Plugins)
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
                }
                else
                {
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
                        SyncedAt = now
                    });
                }
            }

            var etag = response.Headers.ETag?.Tag;
            repo.ETag = etag;
            repo.LastSyncedAt = now;
            repo.LastSyncError = null;

            await _db.SaveChangesAsync(ct);

            sw.Stop();
            _logger.LogInformation(
                "SyncRepository complete — RepositoryId={RepositoryId}, PluginCount={PluginCount}, Duration={Duration}ms, ETag={ETag}",
                repoId, manifest.Plugins.Count, sw.ElapsedMilliseconds, etag);
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
        return await _db.PluginManifests
            .Include(m => m.Repository)
            .Where(m => m.Repository.Enabled)
            .OrderBy(m => m.Name)
            .ThenByDescending(m => m.Version)
            .AsNoTracking()
            .ToListAsync(ct);
    }
}

internal sealed record GitHubContentResponse(
    string? Content,
    string? Encoding,
    string? Sha);

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
