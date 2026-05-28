using System.Diagnostics;
using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Mone.Contracts.Plugins;
using Mone.Infrastructure.Data;
using Mone.Infrastructure.Data.Entities;
using Mone.Messaging;
using Mone.Messaging.Messages;
using Mone.ProbeExecutor.Services;
using NATS.Client.JetStream;
using Quartz;

namespace Mone.ProbeExecutor.Jobs;

public sealed class ProbeExecutionJob(
    Mone.PluginEngine.PluginEngine pluginEngine,
    MoneDbContext db,
    INatsJSContext jetStream,
    ILogger<ProbeExecutionJob> logger) : IJob
{
    public async Task Execute(IJobExecutionContext context)
    {
        var probePluginId = context.MergedJobDataMap.GetString("ProbePluginId")!;
        var targetId = context.MergedJobDataMap.GetString("TargetId")!;
        var assignmentId = context.MergedJobDataMap.GetString("AssignmentId")!;
        var targetAddressOverride = context.MergedJobDataMap.ContainsKey("TargetAddressOverride")
            ? context.MergedJobDataMap.GetString("TargetAddressOverride")
            : null;
        var hostAddress = context.MergedJobDataMap.ContainsKey("HostAddress")
            ? context.MergedJobDataMap.GetString("HostAddress")
            : null;

        logger.LogInformation("Executing probe {ProbeId} for target {TargetId}", probePluginId, targetId);

        var registration = pluginEngine.Registry.Get(probePluginId);
        if (registration is null)
        {
            logger.LogError("Probe plugin {ProbeId} not found in registry", probePluginId);
            return;
        }

        var probe = registration.Plugin as IProbePlugin;
        if (probe is null)
        {
            logger.LogError("Plugin {ProbeId} is not an IProbePlugin", probePluginId);
            return;
        }

        var mergedConfig = await BuildMergedConfigAsync(probePluginId, assignmentId, context.CancellationToken);

        var pluginContext = new PluginContext(probePluginId, mergedConfig, context.CancellationToken);
        await probe.InitializeAsync(pluginContext);

        var sw = Stopwatch.StartNew();
        try
        {
            var probeAddress = targetAddressOverride ?? hostAddress ?? targetId;
            if (targetAddressOverride is not null)
            {
                logger.LogDebug("Using TargetAddressOverride {Override} instead of host address for probe {ProbeId}",
                    targetAddressOverride, probePluginId);
            }

            var result = await probe.ExecuteAsync(probeAddress, context.CancellationToken);
            sw.Stop();

            logger.LogInformation(
                "Probe {ProbeId} completed for target {TargetId}: {Status} in {DurationMs}ms",
                probePluginId, targetId, result.Status, sw.ElapsedMilliseconds);

            var probeType = registration.Metadata.Name;
            var subject = $"probes.results.{probeType}.{targetId}";
            var message = new ProbeResultMessage(targetId, probePluginId, probeType, result);

            try
            {
                await jetStream.PublishAsync(subject, message);
                logger.LogDebug("Published probe result to NATS subject {Subject}", subject);
            }
            catch (Exception ex)
            {
                logger.LogError(ex, "Failed to publish probe result to NATS for {ProbeId}/{TargetId}",
                    probePluginId, targetId);
            }

            var entity = new ProbeResultEntity
            {
                Timestamp = result.Timestamp,
                TargetId = Guid.Parse(targetId),
                ProbeId = probePluginId,
                Status = result.Status,
                Summary = result.Summary,
                DurationMs = result.Duration.TotalMilliseconds,
                MetadataJson = result.Metadata is not null ? JsonSerializer.Serialize(result.Metadata) : null
            };

            try
            {
                db.ProbeResults.Add(entity);
                await db.SaveChangesAsync(context.CancellationToken);
                logger.LogDebug("Stored probe result in database for {ProbeId}/{TargetId}", probePluginId, targetId);
            }
            catch (Exception ex)
            {
                logger.LogError(ex, "Failed to store probe result in database for {ProbeId}/{TargetId}",
                    probePluginId, targetId);
            }
        }
        catch (Exception ex)
        {
            sw.Stop();
            logger.LogError(ex,
                "Probe {ProbeId} execution failed for target {TargetId} after {DurationMs}ms",
                probePluginId, targetId, sw.ElapsedMilliseconds);
        }
    }

    internal async Task<IReadOnlyDictionary<string, string>> BuildMergedConfigAsync(
        string probePluginId, string assignmentId, CancellationToken ct)
    {
        var assignment = await db.ProbeAssignments
            .AsNoTracking()
            .FirstOrDefaultAsync(a => a.Id == Guid.Parse(assignmentId), ct);

        return await Mone.Infrastructure.Services.ConfigMerger.BuildMergedConfigAsync(
            db, probePluginId, assignment?.ConfigJson, logger, ct);
    }
}
