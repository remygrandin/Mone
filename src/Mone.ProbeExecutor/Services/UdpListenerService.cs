using System.Net;
using System.Net.Sockets;
using System.Text.Json;
using Mone.Contracts.Plugins;
using Mone.Infrastructure.Data;
using Mone.Infrastructure.Data.Entities;
using Mone.Messaging.Messages;
using NATS.Client.JetStream;

namespace Mone.ProbeExecutor.Services;

public sealed class UdpListenerService(
    Mone.PluginEngine.PluginEngine pluginEngine,
    IServiceScopeFactory scopeFactory,
    ILogger<UdpListenerService> logger) : BackgroundService
{
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        var registrations = pluginEngine.Registry
            .GetAll()
            .Where(r => r.Metadata.Kind == Mone.PluginEngine.PluginKind.Probe
                      && r.Plugin is IPassiveUdpPlugin)
            .ToList();

        if (registrations.Count == 0)
        {
            logger.LogInformation("No IPassiveUdpPlugin plugins registered, UDP listener will not start");
            return;
        }

        // Group plugins by port — multiple plugins may share a port
        var pluginsByPort = registrations
            .GroupBy(r => ((IPassiveUdpPlugin)r.Plugin).UdpPort)
            .ToDictionary(g => g.Key, g => g.ToList());

        logger.LogInformation("Starting UDP listeners on {PortCount} port(s) for {PluginCount} plugin(s)",
            pluginsByPort.Count, registrations.Count);

        var tasks = new List<Task>();
        foreach (var (port, plugins) in pluginsByPort)
        {
            tasks.Add(RunReceiveLoopAsync(port, plugins, stoppingToken));
        }

        await Task.WhenAll(tasks);
    }

    private async Task RunReceiveLoopAsync(
        int port,
        List<Mone.PluginEngine.PluginRegistration> registrations,
        CancellationToken ct)
    {
        UdpClient? udpClient = null;
        try
        {
            udpClient = new UdpClient(port);
            logger.LogInformation("UDP listener started on port {Port}, serving {PluginCount} plugin(s)",
                port, registrations.Count);
        }
        catch (SocketException ex)
        {
            logger.LogError(ex, "Failed to bind UDP socket on port {Port}", port);
            return;
        }

        using (udpClient)
        {
            while (!ct.IsCancellationRequested)
            {
                UdpReceiveResult datagram;
                try
                {
                    datagram = await udpClient.ReceiveAsync(ct);
                }
                catch (OperationCanceledException) when (ct.IsCancellationRequested)
                {
                    break;
                }
                catch (SocketException ex)
                {
                    logger.LogError(ex, "Socket error receiving datagram on port {Port}", port);
                    continue;
                }

                logger.LogInformation("Datagram received on port {Port} from {RemoteEndPoint}, {ByteCount} bytes",
                    port, datagram.RemoteEndPoint, datagram.Buffer.Length);

                foreach (var registration in registrations)
                {
                    await HandlePluginDatagramAsync(registration, datagram, ct);
                }
            }
        }

        logger.LogInformation("UDP listener on port {Port} stopped", port);
    }

    private async Task HandlePluginDatagramAsync(
        Mone.PluginEngine.PluginRegistration registration,
        UdpReceiveResult datagram,
        CancellationToken ct)
    {
        var plugin = (IPassiveUdpPlugin)registration.Plugin;
        var pluginId = registration.Metadata.PluginId;
        var probeType = registration.Metadata.Name;

        try
        {
            var result = await plugin.HandleDatagramAsync(
                datagram.Buffer,
                datagram.RemoteEndPoint,
                ct);

            if (result is null)
            {
                logger.LogDebug("Plugin {PluginId} returned null for datagram from {RemoteEndPoint}, skipping",
                    pluginId, datagram.RemoteEndPoint);
                return;
            }

            logger.LogInformation(
                "Plugin {PluginId} processed datagram from {RemoteEndPoint}: {Status}",
                pluginId, datagram.RemoteEndPoint, result.Status);

            var targetId = datagram.RemoteEndPoint.Address.ToString();
            var subject = $"probes.results.{probeType}.{targetId}";
            var message = new ProbeResultMessage(targetId, pluginId, probeType, result);

            using var scope = scopeFactory.CreateScope();

            // Publish to NATS
            try
            {
                var jetStream = scope.ServiceProvider.GetRequiredService<INatsJSContext>();
                await jetStream.PublishAsync(subject, message, cancellationToken: ct);
                logger.LogDebug("Published UDP probe result to NATS subject {Subject}", subject);
            }
            catch (Exception ex)
            {
                logger.LogError(ex, "Failed to publish UDP probe result to NATS for plugin {PluginId}", pluginId);
            }

            // Persist to DB
            try
            {
                var db = scope.ServiceProvider.GetRequiredService<MoneDbContext>();
                var entity = new ProbeResultEntity
                {
                    Timestamp = result.Timestamp,
                    TargetId = Guid.Empty, // UDP datagrams don't have a pre-assigned target
                    ProbeId = pluginId,
                    Status = result.Status,
                    Summary = result.Summary,
                    DurationMs = result.Duration.TotalMilliseconds,
                    MetadataJson = result.Metadata is not null ? JsonSerializer.Serialize(result.Metadata) : null
                };

                db.ProbeResults.Add(entity);
                await db.SaveChangesAsync(ct);
                logger.LogDebug("Stored UDP probe result in database for plugin {PluginId}", pluginId);
            }
            catch (Exception ex)
            {
                logger.LogError(ex, "Failed to store UDP probe result in database for plugin {PluginId}", pluginId);
            }
        }
        catch (OperationCanceledException) when (ct.IsCancellationRequested)
        {
            // Shutting down — don't log as error
        }
        catch (Exception ex)
        {
            logger.LogWarning(ex, "Plugin {PluginId} failed to handle datagram from {RemoteEndPoint}",
                pluginId, datagram.RemoteEndPoint);
        }
    }
}
