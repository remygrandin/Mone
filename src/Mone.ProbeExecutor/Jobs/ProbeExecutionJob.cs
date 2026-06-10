using System.Diagnostics;
using System.Text.Json;
using Mone.Contracts.Models;
using Mone.Contracts.Plugins;
using Mone.Messaging.Messages;
using Mone.ProbeExecutor.Services;
using Quartz;

namespace Mone.ProbeExecutor.Jobs;

public sealed class ProbeExecutionJob(
    Mone.PluginEngine.PluginEngine pluginEngine,
    IResultSink resultSink,
    ILogger<ProbeExecutionJob> logger) : IJob
{
    public async Task Execute(IJobExecutionContext context)
    {
        var probePluginId = context.MergedJobDataMap.GetString("ProbePluginId")!;
        var targetId = context.MergedJobDataMap.GetString("TargetId")!;
        var targetAddressOverride = context.MergedJobDataMap.ContainsKey("TargetAddressOverride")
            ? context.MergedJobDataMap.GetString("TargetAddressOverride")
            : null;
        var hostAddress = context.MergedJobDataMap.ContainsKey("HostAddress")
            ? context.MergedJobDataMap.GetString("HostAddress")
            : null;
        var nameSnakeCase = context.MergedJobDataMap.ContainsKey("NameSnakeCase")
            ? context.MergedJobDataMap.GetString("NameSnakeCase")
            : null;
        var mergedConfig = ParseMergedConfig(context.MergedJobDataMap.GetString("MergedConfigJson"));

        logger.LogInformation("Executing probe {ProbeId} for target {TargetId}", probePluginId, targetId);

        var registration = pluginEngine.Registry.Get(probePluginId);
        if (registration is null)
        {
            logger.LogError("Probe plugin {ProbeId} not found in registry", probePluginId);
            return;
        }

        var probe = ResolveProbeInstance(registration);
        if (probe is null)
        {
            logger.LogError("Plugin {ProbeId} is not an IProbePlugin", probePluginId);
            return;
        }

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

            var rawResult = await probe.ExecuteAsync(probeAddress, context.CancellationToken);
            sw.Stop();

            var result = PrefixMetadataKeys(rawResult, nameSnakeCase);

            logger.LogInformation(
                "Probe {ProbeId} completed for target {TargetId}: {Status} in {DurationMs}ms",
                probePluginId, targetId, result.Status, sw.ElapsedMilliseconds);

            var probeType = registration.Metadata.Name;
            var subject = $"probes.results.{probeType}.{targetId}";
            var message = new ProbeResultMessage(targetId, probePluginId, probeType, result);

            await resultSink.PublishAsync(subject, message, context.CancellationToken);
        }
        catch (Exception ex)
        {
            sw.Stop();
            logger.LogError(ex,
                "Probe {ProbeId} execution failed for target {TargetId} after {DurationMs}ms",
                probePluginId, targetId, sw.ElapsedMilliseconds);
        }
    }

    private static IReadOnlyDictionary<string, string> ParseMergedConfig(string? json)
    {
        if (string.IsNullOrEmpty(json))
            return new Dictionary<string, string>();

        return JsonSerializer.Deserialize<Dictionary<string, string>>(json)
               ?? new Dictionary<string, string>();
    }

    // PerTarget plugins (e.g. Ping) carry per-assignment config in mutable instance fields.
    // The registry holds one shared instance, so concurrent jobs would clobber each other's
    // config via InitializeAsync. Honor the declared mode by giving each execution its own
    // instance; Batch plugins keep the shared registry instance.
    private static IProbePlugin? ResolveProbeInstance(Mone.PluginEngine.PluginRegistration registration)
    {
        if (registration.Metadata.InstantiationMode == InstantiationMode.PerTarget)
            return Activator.CreateInstance(registration.Plugin.GetType()) as IProbePlugin;

        return registration.Plugin as IProbePlugin;
    }

    internal static ProbeResult PrefixMetadataKeys(ProbeResult result, string? nameSnakeCase)
    {
        if (result.Metadata is null || result.Metadata.Count == 0)
            return result;
        if (string.IsNullOrEmpty(nameSnakeCase))
            return result;

        var prefix = nameSnakeCase + ".";
        var rewritten = new Dictionary<string, object>(result.Metadata.Count, StringComparer.Ordinal);
        foreach (var kvp in result.Metadata)
        {
            var key = kvp.Key.Contains('.') ? kvp.Key : prefix + kvp.Key;
            rewritten[key] = kvp.Value;
        }
        return result with { Metadata = rewritten };
    }
}
