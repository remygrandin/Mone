using Mone.Contracts.Models;
using Mone.ProbeExecutor.Jobs;
using Quartz;
using Quartz.Impl.Matchers;

namespace Mone.ProbeExecutor.Services;

public sealed class ProbeSchedulerService(
    Mone.PluginEngine.PluginEngine pluginEngine,
    ISchedulerFactory schedulerFactory,
    IProbeConfigSource configSource,
    IConfiguration configuration,
    ILogger<ProbeSchedulerService> logger) : IHostedService
{
    private const string ProbeGroup = "probes";
    private static readonly TimeSpan PeriodicReconcileInterval = TimeSpan.FromSeconds(60);

    private readonly SemaphoreSlim _reconcileLock = new(1, 1);
    private readonly CancellationTokenSource _stopping = new();
    private Task? _periodicTask;

    public async Task StartAsync(CancellationToken cancellationToken)
    {
        var pluginDir = configuration["ProbeExecutor:PluginDirectory"] ?? "plugins";
        var fullPath = Path.GetFullPath(pluginDir);

        logger.LogInformation("Loading probe plugins from {PluginDirectory}", fullPath);
        pluginEngine.LoadPluginsFromDirectory(fullPath);

        var probeCount = pluginEngine.Registry.CountByKind(Mone.PluginEngine.PluginKind.Probe);
        logger.LogInformation("Loaded {ProbeCount} probe plugin(s)", probeCount);

        await ReconcileAsync(cancellationToken);

        _periodicTask = RunPeriodicReconcileAsync(_stopping.Token);
    }

    public async Task StopAsync(CancellationToken cancellationToken)
    {
        await _stopping.CancelAsync();
        if (_periodicTask is not null)
        {
            try { await _periodicTask.WaitAsync(cancellationToken); }
            catch (OperationCanceledException) { /* shutting down */ }
        }
    }

    /// <summary>
    /// Reconciles the Quartz schedule against the probe specs served by the console API (with local
    /// cache fallback): schedules new probes, reschedules ones whose cron/target/config changed, and
    /// unschedules removed ones. Safe to call concurrently — calls are serialized so the timer and
    /// NATS-driven reconciles cannot interleave Quartz mutations.
    /// </summary>
    public async Task ReconcileAsync(CancellationToken cancellationToken)
    {
        await _reconcileLock.WaitAsync(cancellationToken);
        try
        {
            var specs = await configSource.GetProbeSpecsAsync(cancellationToken);
            var scheduler = await schedulerFactory.GetScheduler(cancellationToken);

            var desired = new Dictionary<JobKey, DesiredProbe>();
            foreach (var spec in specs)
            {
                if (!spec.Enabled)
                    continue;

                var plugin = pluginEngine.Registry.Get(spec.ProbePluginId);
                if (plugin is null)
                {
                    logger.LogWarning("Probe plugin {ProbePluginId} not found, skipping assignment {AssignmentId}",
                        spec.ProbePluginId, spec.AssignmentId);
                    continue;
                }

                if (plugin.Metadata.ProbeMode == ProbeMode.Passive)
                    continue;

                var jobKey = new JobKey($"probe-{spec.AssignmentId}-{spec.HostId}", ProbeGroup);
                desired[jobKey] = new DesiredProbe(spec, ComputeSignature(spec));
            }

            var existingKeys = await scheduler.GetJobKeys(GroupMatcher<JobKey>.GroupEquals(ProbeGroup), cancellationToken);

            var unscheduled = 0;
            foreach (var jobKey in existingKeys)
            {
                if (desired.ContainsKey(jobKey))
                    continue;

                await scheduler.DeleteJob(jobKey, cancellationToken);
                unscheduled++;
                logger.LogInformation("Unscheduled removed probe job {JobKey}", jobKey);
            }

            var scheduled = 0;
            var rescheduled = 0;
            foreach (var (jobKey, probe) in desired)
            {
                var existingDetail = await scheduler.GetJobDetail(jobKey, cancellationToken);
                if (existingDetail is not null)
                {
                    var existingSignature = existingDetail.JobDataMap.GetString("Signature");
                    if (existingSignature == probe.Signature)
                        continue;

                    await scheduler.DeleteJob(jobKey, cancellationToken);
                    rescheduled++;
                    logger.LogInformation("Rescheduling probe job {JobKey} (assignment changed)", jobKey);
                }

                var (job, trigger) = BuildJobAndTrigger(jobKey, probe);
                await scheduler.ScheduleJob(job, trigger, cancellationToken);

                if (existingDetail is null)
                {
                    scheduled++;
                    logger.LogInformation(
                        "Scheduled probe {ProbePluginId} for host {HostId} with cron {Cron}",
                        probe.Spec.ProbePluginId, probe.Spec.HostId, probe.Spec.ScheduleCron);
                }
            }

            logger.LogInformation(
                "Probe reconcile complete: {Desired} effective, {Scheduled} new, {Rescheduled} changed, {Unscheduled} removed",
                desired.Count, scheduled, rescheduled, unscheduled);
        }
        finally
        {
            _reconcileLock.Release();
        }
    }

    private async Task RunPeriodicReconcileAsync(CancellationToken cancellationToken)
    {
        using var timer = new PeriodicTimer(PeriodicReconcileInterval);
        try
        {
            while (await timer.WaitForNextTickAsync(cancellationToken))
            {
                try
                {
                    await ReconcileAsync(cancellationToken);
                }
                catch (OperationCanceledException)
                {
                    break;
                }
                catch (Exception ex)
                {
                    logger.LogError(ex, "Periodic probe reconcile failed; will retry on next tick");
                }
            }
        }
        catch (OperationCanceledException)
        {
            /* shutting down */
        }
    }

    private static (IJobDetail Job, ITrigger Trigger) BuildJobAndTrigger(JobKey jobKey, DesiredProbe probe)
    {
        var spec = probe.Spec;

        var jobBuilder = JobBuilder.Create<ProbeExecutionJob>()
            .WithIdentity(jobKey)
            .UsingJobData("ProbePluginId", spec.ProbePluginId)
            .UsingJobData("TargetId", spec.HostId.ToString())
            .UsingJobData("HostAddress", spec.HostAddress)
            .UsingJobData("AssignmentId", spec.AssignmentId.ToString())
            .UsingJobData("MergedConfigJson", SerializeConfig(spec.MergedConfig))
            .UsingJobData("Signature", probe.Signature);

        if (spec.TargetAddressOverride is not null)
            jobBuilder.UsingJobData("TargetAddressOverride", spec.TargetAddressOverride);

        if (spec.NameSnakeCase is not null)
            jobBuilder.UsingJobData("NameSnakeCase", spec.NameSnakeCase);

        var job = jobBuilder.Build();

        var trigger = TriggerBuilder.Create()
            .WithIdentity($"trigger-{spec.AssignmentId}-{spec.HostId}", ProbeGroup)
            .ForJob(job)
            .WithCronSchedule(ToQuartzCron(spec.ScheduleCron))
            .StartNow()
            .Build();

        return (job, trigger);
    }

    private static string SerializeConfig(IReadOnlyDictionary<string, string> config) =>
        System.Text.Json.JsonSerializer.Serialize(config);

    /// <summary>
    /// Identity of the scheduled job's inputs. When this changes, the job is torn down and
    /// recreated; when it matches, an existing job is left untouched so its next-fire time and
    /// trigger state survive the reconcile.
    /// </summary>
    private static string ComputeSignature(ProbeSpec spec) =>
        string.Join('|',
            spec.ProbePluginId,
            spec.ScheduleCron,
            spec.HostAddress,
            spec.TargetAddressOverride ?? "",
            spec.NameSnakeCase ?? "",
            SerializeConfig(spec.MergedConfig));

    /// <summary>
    /// Quartz.NET requires 6-7 field cron (sec min hour dom mon dow [year]).
    /// Standard unix cron is 5 fields (min hour dom mon dow).
    /// Prepend "0 " (fire at second 0) when we detect a 5-field expression.
    /// </summary>
    private static string ToQuartzCron(string cron)
    {
        var parts = cron.Trim().Split(' ', StringSplitOptions.RemoveEmptyEntries);
        if (parts.Length != 5) return cron;

        // Unix: min hour dom month dow → Quartz: sec min hour dom month dow
        var dom = parts[2];
        var dow = parts[4];

        // Quartz requires exactly one of dom/dow to be '?'
        if (dow == "*")
            dow = "?";
        else if (dom == "*")
            dom = "?";

        return $"0 {parts[0]} {parts[1]} {dom} {parts[3]} {dow}";
    }

    private sealed record DesiredProbe(ProbeSpec Spec, string Signature);
}
