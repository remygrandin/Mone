using System.Net.Http.Json;
using System.Text.Json;
using Mone.Contracts.Models;
using Mone.Infrastructure.Services;
using Mone.ProbeExecutor.Data;

namespace Mone.ProbeExecutor.Services;

/// <summary>
/// Pulls fully-resolved probe specs from the console API and caches them in local SQLite. When the
/// API is unreachable, callers transparently fall back to the cached snapshot so the executor keeps
/// running on its last-known config through a console/network outage.
/// </summary>
public sealed class ApiProbeConfigSource(
    IHttpClientFactory httpClientFactory,
    SpoolStore spoolStore,
    ResolvedNodeIdentity nodeIdentity,
    IConfiguration configuration,
    ILogger<ApiProbeConfigSource> logger) : IProbeConfigSource
{
    private static readonly IReadOnlyList<ProbeSpec> Empty = [];

    public async Task<IReadOnlyList<ProbeSpec>> GetProbeSpecsAsync(CancellationToken ct)
    {
        var baseUrl = configuration["Mone:Api:BaseUrl"];
        if (string.IsNullOrWhiteSpace(baseUrl))
        {
            logger.LogWarning("Mone:Api:BaseUrl is not configured; falling back to cached probe config");
            return await GetCachedSpecsAsync(ct);
        }

        var url = $"{baseUrl.TrimEnd('/')}/api/executor-nodes/{nodeIdentity.Id}/probe-assignments";

        try
        {
            using var client = httpClientFactory.CreateClient();
            using var request = new HttpRequestMessage(HttpMethod.Get, url);

            var token = configuration["Mone:Node:Token"];
            if (!string.IsNullOrEmpty(token))
                request.Headers.Add("X-Node-Token", token);

            using var response = await client.SendAsync(request, ct);
            response.EnsureSuccessStatusCode();

            var specs = await response.Content.ReadFromJsonAsync<List<ProbeSpec>>(cancellationToken: ct)
                        ?? new List<ProbeSpec>();

            await spoolStore.SaveSnapshotAsync(JsonSerializer.Serialize(specs), ct);
            logger.LogDebug("Fetched {Count} probe spec(s) from API and cached them", specs.Count);
            return specs;
        }
        catch (Exception ex)
        {
            var cached = await GetCachedSpecsAsync(ct);
            logger.LogWarning(ex,
                "Could not fetch probe config from {Url}; running on cached config ({Count} spec(s))",
                url, cached.Count);
            return cached;
        }
    }

    public async Task<IReadOnlyList<ProbeSpec>> GetCachedSpecsAsync(CancellationToken ct)
    {
        var json = await spoolStore.LoadSnapshotAsync(ct);
        if (string.IsNullOrEmpty(json))
            return Empty;

        try
        {
            return JsonSerializer.Deserialize<List<ProbeSpec>>(json) ?? Empty;
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Cached probe config snapshot is corrupt; ignoring it");
            return Empty;
        }
    }
}
