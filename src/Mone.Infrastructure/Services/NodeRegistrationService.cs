using System.Net.Http.Json;
using System.Reflection;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

namespace Mone.Infrastructure.Services;

/// <summary>
/// Registers this executor with the API on startup and posts a heartbeat on a fixed interval so the
/// node registry can show live health. Registration/heartbeat go node → API over HTTP, so a remote
/// executor needs only the API URL (and optional shared token), never direct Postgres access for
/// liveness.
/// </summary>
public sealed class NodeRegistrationService(
    ResolvedNodeIdentity identity,
    IHttpClientFactory httpClientFactory,
    IConfiguration config,
    ILogger<NodeRegistrationService> logger) : BackgroundService
{
    private static readonly TimeSpan HeartbeatInterval = TimeSpan.FromSeconds(30);

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        var baseUrl = config["Mone:Api:BaseUrl"];
        if (string.IsNullOrWhiteSpace(baseUrl))
        {
            logger.LogWarning(
                "Mone:Api:BaseUrl not configured; node registration/heartbeat disabled for node {NodeName}",
                identity.Name);
            return;
        }

        var token = config["Mone:Node:Token"];
        var version = Assembly.GetEntryAssembly()?
            .GetCustomAttribute<AssemblyInformationalVersionAttribute>()?.InformationalVersion
            ?? Assembly.GetEntryAssembly()?.GetName().Version?.ToString();
        var address = config["Mone:Node:Address"];

        var client = httpClientFactory.CreateClient();
        client.BaseAddress = new Uri(baseUrl.TrimEnd('/') + "/");
        if (!string.IsNullOrEmpty(token))
            client.DefaultRequestHeaders.Add("X-Node-Token", token);

        var registered = await TryRegisterAsync(client, version, address, stoppingToken);

        using var timer = new PeriodicTimer(HeartbeatInterval);
        while (await timer.WaitForNextTickAsync(stoppingToken))
        {
            if (!registered)
            {
                registered = await TryRegisterAsync(client, version, address, stoppingToken);
                continue;
            }

            try
            {
                var resp = await client.PostAsJsonAsync(
                    $"api/executor-nodes/{identity.Id}/heartbeat",
                    new { version },
                    stoppingToken);

                if (resp.StatusCode == System.Net.HttpStatusCode.NotFound)
                {
                    logger.LogInformation("Node {NodeId} unknown to API; re-registering", identity.Id);
                    registered = await TryRegisterAsync(client, version, address, stoppingToken);
                }
                else if (!resp.IsSuccessStatusCode)
                {
                    logger.LogWarning("Heartbeat for node {NodeId} returned {Status}", identity.Id, resp.StatusCode);
                }
            }
            catch (OperationCanceledException)
            {
                break;
            }
            catch (Exception ex)
            {
                logger.LogWarning(ex, "Heartbeat for node {NodeId} failed; will retry", identity.Id);
            }
        }
    }

    private async Task<bool> TryRegisterAsync(HttpClient client, string? version, string? address, CancellationToken ct)
    {
        try
        {
            var resp = await client.PostAsJsonAsync(
                "api/executor-nodes/register",
                new
                {
                    id = identity.Id,
                    name = identity.Name,
                    hostname = Environment.MachineName,
                    address,
                    role = (int)identity.Role,
                    version
                },
                ct);

            if (resp.IsSuccessStatusCode)
            {
                logger.LogInformation(
                    "Registered node {NodeName} ({NodeId}) as {Role}", identity.Name, identity.Id, identity.Role);
                return true;
            }

            logger.LogWarning("Node registration returned {Status} for {NodeName}", resp.StatusCode, identity.Name);
            return false;
        }
        catch (OperationCanceledException)
        {
            return false;
        }
        catch (Exception ex)
        {
            logger.LogWarning(ex, "Node registration failed for {NodeName}; will retry on next heartbeat", identity.Name);
            return false;
        }
    }
}
