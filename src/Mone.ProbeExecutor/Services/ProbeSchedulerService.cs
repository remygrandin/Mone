using Microsoft.EntityFrameworkCore;
using Mone.Contracts.Models;
using Mone.Infrastructure.Data;
using Mone.Infrastructure.Services;
using Mone.ProbeExecutor.Jobs;
using Quartz;

namespace Mone.ProbeExecutor.Services;

public sealed class ProbeSchedulerService(
    Mone.PluginEngine.PluginEngine pluginEngine,
    ISchedulerFactory schedulerFactory,
    IServiceScopeFactory scopeFactory,
    IConfiguration configuration,
    ILogger<ProbeSchedulerService> logger) : IHostedService
{
    public async Task StartAsync(CancellationToken cancellationToken)
    {
        var pluginDir = configuration["ProbeExecutor:PluginDirectory"] ?? "plugins";
        var fullPath = Path.GetFullPath(pluginDir);

        logger.LogInformation("Loading probe plugins from {PluginDirectory}", fullPath);
        pluginEngine.LoadPluginsFromDirectory(fullPath);

        var probeCount = pluginEngine.Registry.CountByKind(Mone.PluginEngine.PluginKind.Probe);
        logger.LogInformation("Loaded {ProbeCount} probe plugin(s)", probeCount);

        using var scope = scopeFactory.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<MoneDbContext>();
        var resolver = scope.ServiceProvider.GetRequiredService<InheritanceResolver>();

        var hosts = await db.Hosts.ToListAsync(cancellationToken);
        var scheduler = await schedulerFactory.GetScheduler(cancellationToken);
        var scheduledCount = 0;

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
                {
                    logger.LogInformation(
                        "Skipping Quartz scheduling for passive plugin {ProbePluginId} (ProbeMode={ProbeMode}), handled via HTTP endpoint",
                        assignment.ProbePluginId, plugin.Metadata.ProbeMode);
                    continue;
                }

                var jobKey = new JobKey($"probe-{assignment.AssignmentId}-{host.Id}", "probes");

                var jobBuilder = JobBuilder.Create<ProbeExecutionJob>()
                    .WithIdentity(jobKey)
                    .UsingJobData("ProbePluginId", assignment.ProbePluginId)
                    .UsingJobData("TargetId", host.Id.ToString())
                    .UsingJobData("HostAddress", host.Address)
                    .UsingJobData("AssignmentId", assignment.AssignmentId.ToString());

                if (assignment.TargetAddressOverride is not null)
                    jobBuilder.UsingJobData("TargetAddressOverride", assignment.TargetAddressOverride);

                var job = jobBuilder.Build();

                var quartzCron = ToQuartzCron(assignment.ScheduleCron);

                var trigger = TriggerBuilder.Create()
                    .WithIdentity($"trigger-{assignment.AssignmentId}-{host.Id}", "probes")
                    .WithCronSchedule(quartzCron)
                    .StartNow()
                    .Build();

                await scheduler.ScheduleJob(job, trigger, cancellationToken);
                scheduledCount++;

                var sourceLabel = assignment.SourceType == AssignmentSourceType.Inherited
                    ? $"inherited from group {assignment.SourceGroupId}"
                    : "direct";

                logger.LogInformation(
                    "Scheduled probe {ProbePluginId} for host {HostId} with cron {Cron} ({Source})",
                    assignment.ProbePluginId, host.Id, assignment.ScheduleCron, sourceLabel);
            }
        }

        logger.LogInformation("Scheduled {ScheduledCount} effective probe assignment(s) across {HostCount} host(s)",
            scheduledCount, hosts.Count);
    }

    public Task StopAsync(CancellationToken cancellationToken) => Task.CompletedTask;

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
}
