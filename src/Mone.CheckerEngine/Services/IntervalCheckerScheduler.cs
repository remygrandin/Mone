using System.Collections.Concurrent;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Mone.Contracts.Models;
using Mone.Contracts.Plugins;
using Mone.Infrastructure.Data;
using Mone.Infrastructure.Services;

namespace Mone.CheckerEngine.Services;

public sealed class IntervalCheckerScheduler(
    IServiceScopeFactory scopeFactory,
    Mone.PluginEngine.PluginEngine pluginEngine,
    CheckerDispatcher dispatcher,
    ILogger<IntervalCheckerScheduler> logger) : BackgroundService
{
    private static readonly TimeSpan TickPeriod = TimeSpan.FromSeconds(15);

    private readonly ConcurrentDictionary<string, DateTimeOffset> _lastRun = new();

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        logger.LogInformation(
            "IntervalCheckerScheduler starting, tick period={TickPeriod}", TickPeriod);

        try { await Task.Delay(TickPeriod, stoppingToken); }
        catch (OperationCanceledException) { return; }

        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                await TickAsync(stoppingToken);
            }
            catch (OperationCanceledException)
            {
                break;
            }
            catch (Exception ex)
            {
                logger.LogError(ex, "IntervalCheckerScheduler tick failed");
            }

            try { await Task.Delay(TickPeriod, stoppingToken); }
            catch (OperationCanceledException) { break; }
        }
    }

    private async Task TickAsync(CancellationToken ct)
    {
        using var scope = scopeFactory.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<MoneDbContext>();
        var resolver = scope.ServiceProvider.GetRequiredService<InheritanceResolver>();

        var hostIds = await db.Hosts.AsNoTracking().Select(h => h.Id).ToListAsync(ct);
        if (hostIds.Count == 0)
            return;

        var now = DateTimeOffset.UtcNow;

        foreach (var hostId in hostIds)
        {
            var effectiveAssignments = await resolver.GetEffectiveCheckerAssignmentsAsync(hostId);
            foreach (var assignment in effectiveAssignments)
            {
                if (!assignment.Enabled)
                    continue;

                var registration = pluginEngine.Registry.Get(assignment.CheckerPluginId);
                if (registration?.Plugin is not ICheckerPlugin checker)
                    continue;
                if (!checker.InvocationMode.HasFlag(CheckerInvocationMode.OnInterval))
                    continue;

                if (checker.Interval is not TimeSpan interval || interval <= TimeSpan.Zero)
                {
                    logger.LogWarning(
                        "Plugin {CheckerId} declares OnInterval but does not provide a positive Interval; skipping",
                        assignment.CheckerPluginId);
                    continue;
                }

                var key = $"{hostId}:{assignment.CheckerPluginId}";
                if (_lastRun.TryGetValue(key, out var last) && now - last < interval)
                    continue;

                _lastRun[key] = now;

                await dispatcher.DispatchAsync(
                    assignment,
                    hostId.ToString(),
                    triggeringProbeId: null,
                    triggeringResult: null,
                    db,
                    ct);
            }
        }
    }
}
