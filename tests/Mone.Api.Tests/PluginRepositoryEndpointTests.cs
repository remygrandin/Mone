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

    private static string BuildGitHubManifestResponse(string manifestJson)
    {
        var base64 = Convert.ToBase64String(Encoding.UTF8.GetBytes(manifestJson));
        return JsonSerializer.Serialize(new { content = base64, encoding = "base64", sha = "abc123" });
    }

    private static string BuildManifestJson(
        string name = "TestPlugin",
        string version = "1.0.0",
        string downloadUrl = "https://github.com/test/repo/releases/download/v1.0/TestPlugin.zip",
        string? sha256 = null)
    {
        sha256 ??= "0000000000000000000000000000000000000000000000000000000000000000";
        return JsonSerializer.Serialize(new
        {
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
        var manifestJson = BuildManifestJson(
            name: $"SyncPlugin-{Guid.NewGuid():N}",
            version: "2.0.0");
        var ghResponse = BuildGitHubManifestResponse(manifestJson);

        var handler = new MockHttpHandler();
        handler.AddResponse("api.github.com", req =>
            new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new StringContent(ghResponse, Encoding.UTF8, "application/json"),
                Headers = { ETag = new System.Net.Http.Headers.EntityTagHeaderValue("\"test-etag\"") }
            });

        using var client = await CreateAuthClientWithHandler(handler,
            $"sync_valid_{Guid.NewGuid():N}@test.com");

        var createResp = await client.PostAsJsonAsync("/api/plugin-repos",
            new AddRepositoryRequest("sync-owner", "sync-repo", "main", "Sync Test"));
        var created = await createResp.Content.ReadFromJsonAsync<PluginRepositoryResponse>();

        var syncResp = await client.PostAsync($"/api/plugin-repos/{created!.Id}/sync", null);
        Assert.Equal(HttpStatusCode.OK, syncResp.StatusCode);

        var pluginsResp = await client.GetAsync("/api/plugins");
        Assert.Equal(HttpStatusCode.OK, pluginsResp.StatusCode);
        var plugins = await pluginsResp.Content.ReadFromJsonAsync<PluginManifestResponse[]>();
        Assert.NotNull(plugins);
        Assert.Contains(plugins, p => p.Version == "2.0.0" && p.PluginType == "AlertChannel");
    }

    [Fact]
    public async Task SyncRepo_GitHubReturns404_RepoMarkedWithError()
    {
        var handler = new MockHttpHandler();
        handler.AddResponse("api.github.com", _ =>
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
        var manifestJson = BuildManifestJson(name: $"EtagPlugin-{Guid.NewGuid():N}");
        var ghResponse = BuildGitHubManifestResponse(manifestJson);

        var callCount = 0;
        var handler = new MockHttpHandler();
        handler.AddResponse("api.github.com", req =>
        {
            callCount++;
            if (callCount == 1)
            {
                return new HttpResponseMessage(HttpStatusCode.OK)
                {
                    Content = new StringContent(ghResponse, Encoding.UTF8, "application/json"),
                    Headers = { ETag = new System.Net.Http.Headers.EntityTagHeaderValue("\"etag-v1\"") }
                };
            }
            return new HttpResponseMessage(HttpStatusCode.NotModified);
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
    public async Task InstallPlugin_WithValidZip_SetsInstalled()
    {
        var (zipBytes, sha256) = CreateTestZip();
        var pluginName = $"InstallPlugin-{Guid.NewGuid():N}";
        var downloadUrl = $"https://github.com/test/repo/releases/download/v1.0/{pluginName}.zip";
        var manifestJson = BuildManifestJson(name: pluginName, sha256: sha256, downloadUrl: downloadUrl);
        var ghResponse = BuildGitHubManifestResponse(manifestJson);

        var handler = new MockHttpHandler();
        handler.AddResponse("api.github.com", _ =>
            new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new StringContent(ghResponse, Encoding.UTF8, "application/json"),
                Headers = { ETag = new System.Net.Http.Headers.EntityTagHeaderValue("\"install-etag\"") }
            });
        handler.AddResponse("github.com", _ =>
            new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new ByteArrayContent(zipBytes)
            });

        using var client = await CreateAuthClientWithHandler(handler,
            $"install_valid_{Guid.NewGuid():N}@test.com");

        var createResp = await client.PostAsJsonAsync("/api/plugin-repos",
            new AddRepositoryRequest("install-owner", "install-repo", DisplayName: "Install Repo"));
        var created = await createResp.Content.ReadFromJsonAsync<PluginRepositoryResponse>();

        await client.PostAsync($"/api/plugin-repos/{created!.Id}/sync", null);

        var pluginsResp = await client.GetAsync("/api/plugins");
        var plugins = await pluginsResp.Content.ReadFromJsonAsync<PluginManifestResponse[]>();
        var manifest = Assert.Single(plugins!, p => p.Name == pluginName);
        Assert.False(manifest.IsInstalled);

        var installResp = await client.PostAsJsonAsync("/api/plugins/install",
            new InstallPluginRequest(manifest.Id));
        Assert.Equal(HttpStatusCode.OK, installResp.StatusCode);

        var pluginsAfter = await (await client.GetAsync("/api/plugins"))
            .Content.ReadFromJsonAsync<PluginManifestResponse[]>();
        var installed = Assert.Single(pluginsAfter!, p => p.Name == pluginName);
        Assert.True(installed.IsInstalled);
        Assert.NotNull(installed.InstalledAt);
    }

    [Fact]
    public async Task InstallPlugin_HashMismatch_Returns500()
    {
        var pluginName = $"BadHashPlugin-{Guid.NewGuid():N}";
        var downloadUrl = $"https://github.com/test/repo/releases/download/v1.0/{pluginName}.zip";
        var manifestJson = BuildManifestJson(
            name: pluginName,
            sha256: "aaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaa",
            downloadUrl: downloadUrl);
        var ghResponse = BuildGitHubManifestResponse(manifestJson);

        var handler = new MockHttpHandler();
        handler.AddResponse("api.github.com", _ =>
            new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new StringContent(ghResponse, Encoding.UTF8, "application/json"),
                Headers = { ETag = new System.Net.Http.Headers.EntityTagHeaderValue("\"hash-etag\"") }
            });
        handler.AddResponse("github.com", _ =>
            new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new ByteArrayContent(Encoding.UTF8.GetBytes("not-a-valid-zip"))
            });

        using var client = await CreateAuthClientWithHandler(handler,
            $"install_badhash_{Guid.NewGuid():N}@test.com");

        var createResp = await client.PostAsJsonAsync("/api/plugin-repos",
            new AddRepositoryRequest("hash-owner", "hash-repo", DisplayName: "Hash Repo"));
        var created = await createResp.Content.ReadFromJsonAsync<PluginRepositoryResponse>();

        await client.PostAsync($"/api/plugin-repos/{created!.Id}/sync", null);

        var pluginsResp = await client.GetAsync("/api/plugins");
        var plugins = await pluginsResp.Content.ReadFromJsonAsync<PluginManifestResponse[]>();
        var manifest = Assert.Single(plugins!, p => p.Name == pluginName);

        var installResp = await client.PostAsJsonAsync("/api/plugins/install",
            new InstallPluginRequest(manifest.Id));

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
    public async Task UninstallPlugin_RemovesInstallState()
    {
        var (zipBytes, sha256) = CreateTestZip();
        var pluginName = $"UninstallPlugin-{Guid.NewGuid():N}";
        var downloadUrl = $"https://github.com/test/repo/releases/download/v1.0/{pluginName}.zip";
        var manifestJson = BuildManifestJson(name: pluginName, sha256: sha256, downloadUrl: downloadUrl);
        var ghResponse = BuildGitHubManifestResponse(manifestJson);

        var handler = new MockHttpHandler();
        handler.AddResponse("api.github.com", _ =>
            new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new StringContent(ghResponse, Encoding.UTF8, "application/json"),
                Headers = { ETag = new System.Net.Http.Headers.EntityTagHeaderValue("\"uninstall-etag\"") }
            });
        handler.AddResponse("github.com", _ =>
            new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new ByteArrayContent(zipBytes)
            });

        using var client = await CreateAuthClientWithHandler(handler,
            $"uninstall_{Guid.NewGuid():N}@test.com");

        var createResp = await client.PostAsJsonAsync("/api/plugin-repos",
            new AddRepositoryRequest("uni-owner", "uni-repo", DisplayName: "Uninstall Repo"));
        var created = await createResp.Content.ReadFromJsonAsync<PluginRepositoryResponse>();

        await client.PostAsync($"/api/plugin-repos/{created!.Id}/sync", null);

        var plugins = await (await client.GetAsync("/api/plugins"))
            .Content.ReadFromJsonAsync<PluginManifestResponse[]>();
        var manifest = Assert.Single(plugins!, p => p.Name == pluginName);

        await client.PostAsJsonAsync("/api/plugins/install", new InstallPluginRequest(manifest.Id));

        var uninstallResp = await client.PostAsJsonAsync("/api/plugins/uninstall",
            new InstallPluginRequest(manifest.Id));
        Assert.Equal(HttpStatusCode.OK, uninstallResp.StatusCode);

        var pluginsAfter = await (await client.GetAsync("/api/plugins"))
            .Content.ReadFromJsonAsync<PluginManifestResponse[]>();
        var uninstalled = Assert.Single(pluginsAfter!, p => p.Name == pluginName);
        Assert.False(uninstalled.IsInstalled);
        Assert.Null(uninstalled.InstalledAt);
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
    private readonly Dictionary<string, Func<HttpRequestMessage, HttpResponseMessage>> _responses = new();

    public void AddResponse(string hostContains, Func<HttpRequestMessage, HttpResponseMessage> factory)
    {
        _responses[hostContains] = factory;
    }

    protected override Task<HttpResponseMessage> SendAsync(
        HttpRequestMessage request, CancellationToken cancellationToken)
    {
        foreach (var (host, factory) in _responses)
        {
            if (request.RequestUri?.Host.Contains(host, StringComparison.OrdinalIgnoreCase) == true)
                return Task.FromResult(factory(request));
        }

        return Task.FromResult(new HttpResponseMessage(HttpStatusCode.NotFound)
        {
            Content = new StringContent($"No mock configured for {request.RequestUri}")
        });
    }
}
