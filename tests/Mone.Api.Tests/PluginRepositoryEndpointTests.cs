using System.IO.Compression;
using System.Net;
using System.Net.Http.Json;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Http;
using Mone.Api.Models;
using Mone.Api.Tests.Fixtures;
using Xunit;

namespace Mone.Api.Tests;

[Collection("Api")]
public class PluginRepositoryEndpointTests
{
    private readonly ApiFixture _fixture;

    public PluginRepositoryEndpointTests(ApiFixture fixture) => _fixture = fixture;

    private const string DefaultReleaseTag = "build-1";
    private const string DefaultManifestAssetUrl =
        "https://github.com/test/repo/releases/download/build-1/mone-plugins.json";

    private static string BuildReleasesJson(
        string tagName = DefaultReleaseTag,
        bool prerelease = false,
        string manifestAssetUrl = DefaultManifestAssetUrl,
        DateTime? publishedAt = null)
    {
        var published = publishedAt ?? DateTime.UtcNow;
        return JsonSerializer.Serialize(new object[]
        {
            new
            {
                tag_name = tagName,
                name = tagName,
                draft = false,
                prerelease,
                created_at = published,
                published_at = published,
                assets = new object[]
                {
                    new
                    {
                        name = "mone-plugins.json",
                        browser_download_url = manifestAssetUrl,
                        size = 4096L
                    }
                }
            }
        });
    }

    private static string BuildManifestJson(
        string name = "TestPlugin",
        string version = "1.0.0",
        string downloadUrl = "https://github.com/test/repo/releases/download/build-1/TestPlugin.zip",
        string? sha256 = null)
    {
        sha256 ??= "0000000000000000000000000000000000000000000000000000000000000000";
        return JsonSerializer.Serialize(new
        {
            schemaVersion = 1,
            generatedAt = DateTime.UtcNow,
            releaseTag = DefaultReleaseTag,
            commit = "abc123",
            plugins = new[]
            {
                new
                {
                    name,
                    version,
                    description = "A test plugin",
                    pluginType = "AlertChannel",
                    downloadUrl,
                    sha256,
                    fileSize = 1024L,
                    author = "Test Author",
                    license = "MIT"
                }
            }
        });
    }

    private static (byte[] zipBytes, string sha256) CreateTestZip()
    {
        using var ms = new MemoryStream();
        using (var archive = new ZipArchive(ms, ZipArchiveMode.Create, leaveOpen: true))
        {
            var entry = archive.CreateEntry("TestPlugin.dll");
            using var entryStream = entry.Open();
            entryStream.Write(Encoding.UTF8.GetBytes("fake-dll-content"));
        }
        var bytes = ms.ToArray();
        var hash = Convert.ToHexStringLower(SHA256.HashData(bytes));
        return (bytes, hash);
    }

    private MockHttpHandler ConfigureGitHubMock(Action<MockHttpHandler> setup)
    {
        var handler = new MockHttpHandler();
        setup(handler);
        return handler;
    }

    private HttpClient CreateClientWithMockHandler(HttpClient authenticatedClient, MockHttpHandler handler)
    {
        return authenticatedClient;
    }

    #region Repository CRUD

    [Fact]
    public async Task CreateRepo_Returns201()
    {
        using var client = await _fixture.CreateAuthenticatedClientAsync(
            $"repo_create_{Guid.NewGuid():N}@test.com", "ValidPass1!");

        var request = new AddRepositoryRequest("test-owner", "test-repo", "main", "Test Repo");
        var response = await client.PostAsJsonAsync("/api/plugin-repos", request);

        Assert.Equal(HttpStatusCode.Created, response.StatusCode);

        var repo = await response.Content.ReadFromJsonAsync<PluginRepositoryResponse>();
        Assert.NotNull(repo);
        Assert.NotEqual(Guid.Empty, repo.Id);
        Assert.Equal("test-owner", repo.Owner);
        Assert.Equal("test-repo", repo.Repo);
        Assert.Equal("main", repo.Branch);
        Assert.Equal("Test Repo", repo.DisplayName);
        Assert.True(repo.Enabled);
    }

    [Fact]
    public async Task CreateRepo_DefaultDisplayName()
    {
        using var client = await _fixture.CreateAuthenticatedClientAsync(
            $"repo_defname_{Guid.NewGuid():N}@test.com", "ValidPass1!");

        var request = new AddRepositoryRequest("owner1", "repo1");
        var response = await client.PostAsJsonAsync("/api/plugin-repos", request);

        Assert.Equal(HttpStatusCode.Created, response.StatusCode);

        var repo = await response.Content.ReadFromJsonAsync<PluginRepositoryResponse>();
        Assert.NotNull(repo);
        Assert.Equal("owner1/repo1", repo.DisplayName);
    }

    [Fact]
    public async Task ListRepos_ReturnsCreated()
    {
        using var client = await _fixture.CreateAuthenticatedClientAsync(
            $"repo_list_{Guid.NewGuid():N}@test.com", "ValidPass1!");

        var uniqueName = $"ListTest-{Guid.NewGuid():N}";
        await client.PostAsJsonAsync("/api/plugin-repos",
            new AddRepositoryRequest("list-owner", "list-repo", DisplayName: uniqueName));

        var response = await client.GetAsync("/api/plugin-repos");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var repos = await response.Content.ReadFromJsonAsync<PluginRepositoryResponse[]>();
        Assert.NotNull(repos);
        Assert.Contains(repos, r => r.DisplayName == uniqueName);
    }

    [Fact]
    public async Task GetRepoById_ReturnsRepo()
    {
        using var client = await _fixture.CreateAuthenticatedClientAsync(
            $"repo_getid_{Guid.NewGuid():N}@test.com", "ValidPass1!");

        var createResp = await client.PostAsJsonAsync("/api/plugin-repos",
            new AddRepositoryRequest("getid-owner", "getid-repo", DisplayName: "GetById Repo"));
        var created = await createResp.Content.ReadFromJsonAsync<PluginRepositoryResponse>();

        var response = await client.GetAsync($"/api/plugin-repos/{created!.Id}");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var repo = await response.Content.ReadFromJsonAsync<PluginRepositoryResponse>();
        Assert.Equal(created.Id, repo!.Id);
        Assert.Equal("getid-owner", repo.Owner);
    }

    [Fact]
    public async Task GetRepoById_NonExistent_Returns404()
    {
        using var client = await _fixture.CreateAuthenticatedClientAsync(
            $"repo_404_{Guid.NewGuid():N}@test.com", "ValidPass1!");

        var response = await client.GetAsync($"/api/plugin-repos/{Guid.NewGuid()}");

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
    }

    [Fact]
    public async Task DeleteRepo_Returns204()
    {
        using var client = await _fixture.CreateAuthenticatedClientAsync(
            $"repo_delete_{Guid.NewGuid():N}@test.com", "ValidPass1!");

        var createResp = await client.PostAsJsonAsync("/api/plugin-repos",
            new AddRepositoryRequest("del-owner", "del-repo", DisplayName: "Delete Repo"));
        var created = await createResp.Content.ReadFromJsonAsync<PluginRepositoryResponse>();

        var deleteResp = await client.DeleteAsync($"/api/plugin-repos/{created!.Id}");

        Assert.Equal(HttpStatusCode.NoContent, deleteResp.StatusCode);

        var getResp = await client.GetAsync($"/api/plugin-repos/{created.Id}");
        Assert.Equal(HttpStatusCode.NotFound, getResp.StatusCode);
    }

    #endregion

    #region Sync

    [Fact]
    public async Task SyncRepo_NonExistent_Returns404()
    {
        using var client = await _fixture.CreateAuthenticatedClientAsync(
            $"sync_404_{Guid.NewGuid():N}@test.com", "ValidPass1!");

        var response = await client.PostAsync($"/api/plugin-repos/{Guid.NewGuid()}/sync", null);

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
    }

    [Fact]
    public async Task SyncRepo_WithValidManifest_PopulatesPlugins()
    {
        // Plugin and repo names are uniquified per test run so the shared
        // Postgres fixture doesn't bleed between tests.
        var runId = Guid.NewGuid().ToString("N");
        var pluginName = $"SyncPlugin-{runId}";
        var releasesJson = BuildReleasesJson();
        var manifestJson = BuildManifestJson(name: pluginName, version: "2.0.0");

        var handler = new MockHttpHandler();
        handler.AddResponse("api.github.com/repos", _ =>
            new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new StringContent(releasesJson, Encoding.UTF8, "application/json"),
                Headers = { ETag = new System.Net.Http.Headers.EntityTagHeaderValue("\"test-etag\"") }
            });
        handler.AddResponse("mone-plugins.json", _ =>
            new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new StringContent(manifestJson, Encoding.UTF8, "application/json")
            });

        using var client = await CreateAuthClientWithHandler(handler, $"sync_valid_{runId}@test.com");

        var createResp = await client.PostAsJsonAsync("/api/plugin-repos",
            new AddRepositoryRequest($"sync-{runId}", "sync-repo", "main", "Sync Test"));
        var created = await createResp.Content.ReadFromJsonAsync<PluginRepositoryResponse>();

        var syncResp = await client.PostAsync($"/api/plugin-repos/{created!.Id}/sync", null);
        Assert.Equal(HttpStatusCode.OK, syncResp.StatusCode);

        // Cross-test pollution from fire-and-forget background syncs on the
        // POST /api/plugin-repos endpoint can insert rows attributed to other
        // tests' repository Ids. Scope the assertion to THIS test's repo by
        // hitting the DB directly.
        using var scope = _fixture.Factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<Mone.Infrastructure.Data.MoneDbContext>();
        var rows = await Microsoft.EntityFrameworkCore.EntityFrameworkQueryableExtensions
            .ToListAsync(db.PluginManifests.Where(m =>
                m.RepositoryId == created.Id && m.Name == pluginName));

        var row = Assert.Single(rows);
        Assert.Equal("2.0.0", row.Version);
        Assert.Equal(DefaultReleaseTag, row.ReleaseTag);
        Assert.False(row.IsPrerelease);
        Assert.Equal("AlertChannel", row.PluginType);
    }

    [Fact]
    public async Task SyncRepo_GitHubReturns404_RepoMarkedWithError()
    {
        var handler = new MockHttpHandler();
        handler.AddResponse("api.github.com/repos", _ =>
            new HttpResponseMessage(HttpStatusCode.NotFound));

        using var client = await CreateAuthClientWithHandler(handler,
            $"sync_gh404_{Guid.NewGuid():N}@test.com");

        var createResp = await client.PostAsJsonAsync("/api/plugin-repos",
            new AddRepositoryRequest("missing-owner", "missing-repo", DisplayName: "Missing Repo"));
        var created = await createResp.Content.ReadFromJsonAsync<PluginRepositoryResponse>();

        var syncResp = await client.PostAsync($"/api/plugin-repos/{created!.Id}/sync", null);
        Assert.Equal(HttpStatusCode.OK, syncResp.StatusCode);

        var repoResp = await client.GetAsync($"/api/plugin-repos/{created.Id}");
        var repo = await repoResp.Content.ReadFromJsonAsync<PluginRepositoryResponse>();
        Assert.NotNull(repo!.LastSyncError);
        Assert.Contains("404", repo.LastSyncError);
    }

    [Fact]
    public async Task SyncRepo_GitHubReturns304_NoManifestChanges()
    {
        var releasesJson = BuildReleasesJson();
        var manifestJson = BuildManifestJson(name: $"EtagPlugin-{Guid.NewGuid():N}");

        var callCount = 0;
        var handler = new MockHttpHandler();
        handler.AddResponse("api.github.com/repos", _ =>
        {
            callCount++;
            if (callCount == 1)
            {
                return new HttpResponseMessage(HttpStatusCode.OK)
                {
                    Content = new StringContent(releasesJson, Encoding.UTF8, "application/json"),
                    Headers = { ETag = new System.Net.Http.Headers.EntityTagHeaderValue("\"etag-v1\"") }
                };
            }
            return new HttpResponseMessage(HttpStatusCode.NotModified);
        });
        handler.AddResponse("mone-plugins.json", _ =>
            new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new StringContent(manifestJson, Encoding.UTF8, "application/json")
            });

        using var client = await CreateAuthClientWithHandler(handler,
            $"sync_304_{Guid.NewGuid():N}@test.com");

        var createResp = await client.PostAsJsonAsync("/api/plugin-repos",
            new AddRepositoryRequest("etag-owner", "etag-repo", DisplayName: "ETag Repo"));
        var created = await createResp.Content.ReadFromJsonAsync<PluginRepositoryResponse>();

        await client.PostAsync($"/api/plugin-repos/{created!.Id}/sync", null);
        var syncResp2 = await client.PostAsync($"/api/plugin-repos/{created.Id}/sync", null);
        Assert.Equal(HttpStatusCode.OK, syncResp2.StatusCode);

        var repo = await (await client.GetAsync($"/api/plugin-repos/{created.Id}"))
            .Content.ReadFromJsonAsync<PluginRepositoryResponse>();
        Assert.Null(repo!.LastSyncError);
        Assert.NotNull(repo.LastSyncedAt);
    }

    #endregion

    #region Install / Uninstall

    [Fact]
    public async Task InstallPlugin_WithValidZip_Succeeds()
    {
        var runId = Guid.NewGuid().ToString("N");
        var (zipBytes, sha256) = CreateTestZip();
        var pluginName = $"InstallPlugin-{runId}";
        var downloadUrl = $"https://github.com/test/repo/releases/download/build-1/{pluginName}.zip";
        var releasesJson = BuildReleasesJson();
        var manifestJson = BuildManifestJson(name: pluginName, sha256: sha256, downloadUrl: downloadUrl);

        var handler = new MockHttpHandler();
        handler.AddResponse("api.github.com/repos", _ =>
            new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new StringContent(releasesJson, Encoding.UTF8, "application/json"),
                Headers = { ETag = new System.Net.Http.Headers.EntityTagHeaderValue("\"install-etag\"") }
            });
        handler.AddResponse("mone-plugins.json", _ =>
            new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new StringContent(manifestJson, Encoding.UTF8, "application/json")
            });
        handler.AddResponse(".zip", _ =>
            new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new ByteArrayContent(zipBytes)
            });

        using var client = await CreateAuthClientWithHandler(handler, $"install_valid_{runId}@test.com");

        var createResp = await client.PostAsJsonAsync("/api/plugin-repos",
            new AddRepositoryRequest($"install-{runId}", "install-repo", DisplayName: "Install Repo"));
        var created = await createResp.Content.ReadFromJsonAsync<PluginRepositoryResponse>();

        await client.PostAsync($"/api/plugin-repos/{created!.Id}/sync", null);

        using var scope = _fixture.Factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<Mone.Infrastructure.Data.MoneDbContext>();
        var versionId = await Microsoft.EntityFrameworkCore.EntityFrameworkQueryableExtensions
            .SingleAsync(db.PluginManifests
                .Where(m => m.RepositoryId == created.Id && m.Name == pluginName)
                .Select(m => m.Id));

        var installResp = await client.PostAsJsonAsync("/api/plugins/install",
            new InstallPluginRequest(versionId));
        Assert.Equal(HttpStatusCode.OK, installResp.StatusCode);
    }

    [Fact]
    public async Task InstallPlugin_HashMismatch_Returns500()
    {
        var runId = Guid.NewGuid().ToString("N");
        var pluginName = $"BadHashPlugin-{runId}";
        var downloadUrl = $"https://github.com/test/repo/releases/download/build-1/{pluginName}.zip";
        var releasesJson = BuildReleasesJson();
        var manifestJson = BuildManifestJson(
            name: pluginName,
            sha256: "aaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaa",
            downloadUrl: downloadUrl);

        var handler = new MockHttpHandler();
        handler.AddResponse("api.github.com/repos", _ =>
            new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new StringContent(releasesJson, Encoding.UTF8, "application/json"),
                Headers = { ETag = new System.Net.Http.Headers.EntityTagHeaderValue("\"hash-etag\"") }
            });
        handler.AddResponse("mone-plugins.json", _ =>
            new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new StringContent(manifestJson, Encoding.UTF8, "application/json")
            });
        handler.AddResponse(".zip", _ =>
            new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new ByteArrayContent(Encoding.UTF8.GetBytes("not-a-valid-zip"))
            });

        using var client = await CreateAuthClientWithHandler(handler, $"install_badhash_{runId}@test.com");

        var createResp = await client.PostAsJsonAsync("/api/plugin-repos",
            new AddRepositoryRequest($"hash-{runId}", "hash-repo", DisplayName: "Hash Repo"));
        var created = await createResp.Content.ReadFromJsonAsync<PluginRepositoryResponse>();

        await client.PostAsync($"/api/plugin-repos/{created!.Id}/sync", null);

        using var scope = _fixture.Factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<Mone.Infrastructure.Data.MoneDbContext>();
        var versionId = await Microsoft.EntityFrameworkCore.EntityFrameworkQueryableExtensions
            .SingleAsync(db.PluginManifests
                .Where(m => m.RepositoryId == created.Id && m.Name == pluginName)
                .Select(m => m.Id));

        var installResp = await client.PostAsJsonAsync("/api/plugins/install",
            new InstallPluginRequest(versionId));

        Assert.Equal(HttpStatusCode.InternalServerError, installResp.StatusCode);
    }

    [Fact]
    public async Task InstallPlugin_NonExistentManifest_Returns500()
    {
        var handler = new MockHttpHandler();
        using var client = await CreateAuthClientWithHandler(handler,
            $"install_noexist_{Guid.NewGuid():N}@test.com");

        var installResp = await client.PostAsJsonAsync("/api/plugins/install",
            new InstallPluginRequest(Guid.NewGuid()));

        Assert.Equal(HttpStatusCode.InternalServerError, installResp.StatusCode);
    }

    [Fact]
    public async Task UninstallPlugin_Succeeds()
    {
        var runId = Guid.NewGuid().ToString("N");
        var (zipBytes, sha256) = CreateTestZip();
        var pluginName = $"UninstallPlugin-{runId}";
        var downloadUrl = $"https://github.com/test/repo/releases/download/build-1/{pluginName}.zip";
        var releasesJson = BuildReleasesJson();
        var manifestJson = BuildManifestJson(name: pluginName, sha256: sha256, downloadUrl: downloadUrl);

        var handler = new MockHttpHandler();
        handler.AddResponse("api.github.com/repos", _ =>
            new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new StringContent(releasesJson, Encoding.UTF8, "application/json"),
                Headers = { ETag = new System.Net.Http.Headers.EntityTagHeaderValue("\"uninstall-etag\"") }
            });
        handler.AddResponse("mone-plugins.json", _ =>
            new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new StringContent(manifestJson, Encoding.UTF8, "application/json")
            });
        handler.AddResponse(".zip", _ =>
            new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new ByteArrayContent(zipBytes)
            });

        using var client = await CreateAuthClientWithHandler(handler, $"uninstall_{runId}@test.com");

        var createResp = await client.PostAsJsonAsync("/api/plugin-repos",
            new AddRepositoryRequest($"uni-{runId}", "uni-repo", DisplayName: "Uninstall Repo"));
        var created = await createResp.Content.ReadFromJsonAsync<PluginRepositoryResponse>();

        await client.PostAsync($"/api/plugin-repos/{created!.Id}/sync", null);

        using var scope = _fixture.Factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<Mone.Infrastructure.Data.MoneDbContext>();
        var versionId = await Microsoft.EntityFrameworkCore.EntityFrameworkQueryableExtensions
            .SingleAsync(db.PluginManifests
                .Where(m => m.RepositoryId == created.Id && m.Name == pluginName)
                .Select(m => m.Id));

        await client.PostAsJsonAsync("/api/plugins/install", new InstallPluginRequest(versionId));

        var uninstallResp = await client.PostAsJsonAsync("/api/plugins/uninstall",
            new UninstallPluginRequest(pluginName));
        Assert.Equal(HttpStatusCode.OK, uninstallResp.StatusCode);
    }

    #endregion

    #region Auth

    [Fact]
    public async Task Unauthenticated_PluginRepos_Returns401()
    {
        using var client = _fixture.Factory.CreateClient();

        var response = await client.GetAsync("/api/plugin-repos");
        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    [Fact]
    public async Task Unauthenticated_Plugins_Returns401()
    {
        using var client = _fixture.Factory.CreateClient();

        var response = await client.GetAsync("/api/plugins");
        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    #endregion

    #region Helper: Authenticated client with mock HTTP handler

    private async Task<HttpClient> CreateAuthClientWithHandler(MockHttpHandler handler, string email)
    {
        var factory = _fixture.Factory.WithWebHostBuilder(builder =>
        {
            builder.ConfigureServices(services =>
            {
                services.Configure<HttpClientFactoryOptions>("GitHub", options =>
                {
                    options.HttpMessageHandlerBuilderActions.Add(b =>
                    {
                        b.PrimaryHandler = handler;
                    });
                });
            });
        });

        var client = factory.CreateClient();

        await client.PostAsJsonAsync("/api/auth/register", new RegisterRequest(email, "ValidPass1!"));
        await _fixture.GrantSuperAdminAsync(email);
        var loginResp = await client.PostAsJsonAsync("/api/auth/login", new LoginRequest(email, "ValidPass1!"));
        var token = await loginResp.Content.ReadFromJsonAsync<TokenResponse>();
        client.DefaultRequestHeaders.Authorization =
            new System.Net.Http.Headers.AuthenticationHeaderValue("Bearer", token!.Token);

        return client;
    }

    #endregion
}

public sealed class MockHttpHandler : HttpMessageHandler
{
    private readonly List<(string Match, Func<HttpRequestMessage, HttpResponseMessage> Factory)> _routes = new();

    /// <summary>
    /// Register a route by substring match against the full request URL.
    /// Routes are evaluated in registration order; the first match wins.
    /// </summary>
    public void AddResponse(string urlContains, Func<HttpRequestMessage, HttpResponseMessage> factory)
    {
        _routes.Add((urlContains, factory));
    }

    protected override Task<HttpResponseMessage> SendAsync(
        HttpRequestMessage request, CancellationToken cancellationToken)
    {
        var url = request.RequestUri?.ToString() ?? string.Empty;
        foreach (var (match, factory) in _routes)
        {
            if (url.Contains(match, StringComparison.OrdinalIgnoreCase))
                return Task.FromResult(factory(request));
        }

        return Task.FromResult(new HttpResponseMessage(HttpStatusCode.NotFound)
        {
            Content = new StringContent($"No mock configured for {request.RequestUri}")
        });
    }
}
