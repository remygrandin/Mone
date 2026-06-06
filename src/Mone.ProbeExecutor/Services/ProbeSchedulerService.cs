using Microsoft.EntityFrameworkCore;
using Mone.Contracts.Models;
using Mone.Infrastructure.Data;
using Mone.Infrastructure.Services;
using Mone.ProbeExecutor.Jobs;
using Quartz;
using Quartz.Impl.Matchers;

namespace Mone.ProbeExecutor.Services;

public sealed class ProbeSchedulerService(
    Mone.PluginEngine.PluginEngine pluginEngine,
    ISchedulerFactory schedulerFactory,
    IServiceScopeFactory scopeFactory,
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
    /// Reconciles the Quartz schedule against the current set of effective probe assignments:
    /// schedules new probes, reschedules ones whose cron/target changed, and unschedules removed
    /// ones. Safe to call concurrently — calls are serialized so the timer and NATS-driven
    /// reconciles cannot interleave Quartz mutations.
    /// </summary>
    public async Task ReconcileAsync(CancellationToken cancellationToken)
    {
        await _reconcileLock.WaitAsync(cancellationToken);
        try
        {
            using var scope = scopeFactory.CreateScope();
            var db = scope.ServiceProvider.GetRequiredService<MoneDbContext>();
            var resolver = scope.ServiceProvider.GetRequiredService<InheritanceResolver>();

            var hosts = await db.Hosts.ToListAsync(cancellationToken);
            var scheduler = await schedulerFactory.GetScheduler(cancellationToken);

            var desired = new Dictionary<JobKey, DesiredProbe>();
            foreach (var host in hosts)
            {
                var effectiveAssignments = await resolver.GetEffectiveProbeAssignmentsAsync(host.Id);

                foreach (var assignment in effectiveAssignments)
                {
                    if (!assignment.Enabled)
                        continue;

                    var plugin = pluginEngine.Registry.Get(assignment.ProbePluginId);
                    if (plugin is null)
                    {
                        logger.LogWarning("Probe plugin {ProbePluginId} not found, skipping assignment {AssignmentId}",
                            assignment.ProbePluginId, assignment.AssignmentId);
                        continue;
                    }

                    if (plugin.Metadata.ProbeMode == ProbeMode.Passive)
                        continue;

                    var jobKey = new JobKey($"probe-{assignment.AssignmentId}-{host.Id}", ProbeGroup);
                    desired[jobKey] = new DesiredProbe(host.Id, host.Address, assignment,
                        ComputeSignature(host.Address, assignment));
                }
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
                    var sourceLabel = probe.Assignment.SourceType == AssignmentSourceType.Inherited
                        ? $"inherited from group {probe.Assignment.SourceGroupId}"
                        : "direct";
                    logger.LogInformation(
                        "Scheduled probe {ProbePluginId} for host {HostId} with cron {Cron} ({Source})",
                        probe.Assignment.ProbePluginId, probe.HostId, probe.Assignment.ScheduleCron, sourceLabel);
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
        var assignment = probe.Assignment;

        var jobBuilder = JobBuilder.Create<ProbeExecutionJob>()
            .WithIdentity(jobKey)
            .UsingJobData("ProbePluginId", assignment.ProbePluginId)
            .UsingJobData("TargetId", probe.HostId.ToString())
            .UsingJobData("HostAddress", probe.HostAddress)
            .UsingJobData("AssignmentId", assignment.AssignmentId.ToString())
            .UsingJobData("Signature", probe.Signature);

        if (assignment.TargetAddressOverride is not null)
            jobBuilder.UsingJobData("TargetAddressOverride", assignment.TargetAddressOverride);

        var job = jobBuilder.Build();

        var trigger = TriggerBuilder.Create()
            .WithIdentity($"trigger-{assignment.AssignmentId}-{probe.HostId}", ProbeGroup)
            .ForJob(job)
            .WithCronSchedule(ToQuartzCron(assignment.ScheduleCron))
            .StartNow()
            .Build();

        return (job, trigger);
    }

    /// <summary>
    /// Identity of the scheduled job's inputs. When this changes, the job is torn down and
    /// recreated; when it matches, an existing job is left untouched so its next-fire time and
    /// trigger state survive the reconcile.
    /// </summary>
    private static string ComputeSignature(string hostAddress, EffectiveProbeAssignment assignment) =>
        string.Join('|',
            assignment.ProbePluginId,
            assignment.ScheduleCron,
            hostAddress,
            assignment.TargetAddressOverride ?? "");

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

    private sealed record DesiredProbe(
        Guid HostId,
        string HostAddress,
        EffectiveProbeAssignment Assignment,
        string Signature);
}
