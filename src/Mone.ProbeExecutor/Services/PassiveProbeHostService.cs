using Mone.Contracts.Models;
using Mone.Contracts.Plugins;

namespace Mone.ProbeExecutor.Services;

/// <summary>
/// Hosts passive probe plugins. Its only jobs are the two the executor is responsible for: arbitrate
/// port collisions so no two plugins bind the same (protocol, port), and hand each plugin a
/// <see cref="PassiveProbeHost"/> carrying its accumulated assignments plus the spooling result sink.
/// Each plugin owns its own listener, protocol decoding, auth, and responses.
/// </summary>
public sealed class PassiveProbeHostService(
    Mone.PluginEngine.PluginEngine pluginEngine,
    IProbeConfigSource configSource,
    IResultSink resultSink,
    ILogger<PassiveProbeHostService> logger) : IHostedService
{
    private static readonly TimeSpan ReconcileInterval = TimeSpan.FromSeconds(60);

    private readonly CancellationTokenSource _stopping = new();
    private readonly List<RunningPassiveProbe> _running = [];
    private Task? _periodicTask;

    public async Task StartAsync(CancellationToken cancellationToken)
    {
        var registrations = pluginEngine.Registry.GetAll()
            .Where(r => r.Metadata.Kind == Mone.PluginEngine.PluginKind.Probe && r.Plugin is IPassiveProbePlugin)
            .ToList();

        if (registrations.Count == 0)
        {
            logger.LogInformation("No passive probe plugins registered; passive host idle");
            return;
        }

        // Port arbitration: a contested (protocol, port) goes to the lowest PluginId so the outcome
        // is deterministic across restarts. Losers are skipped, not crashed.
        var claimed = new Dictionary<(PassiveProtocol Protocol, int Port), string>();
        foreach (var reg in registrations.OrderBy(r => r.Metadata.PluginId, StringComparer.Ordinal))
        {
            var plugin = (IPassiveProbePlugin)reg.Plugin;
            var key = (plugin.Protocol, plugin.Port);
            if (claimed.TryGetValue(key, out var owner))
            {
                logger.LogWarning(
                    "Port collision: plugin {PluginId} requested {Protocol}/{Port}, already claimed by {Owner}; skipping",
                    reg.Metadata.PluginId, plugin.Protocol, plugin.Port, owner);
                continue;
            }

            claimed[key] = reg.Metadata.PluginId;
            var host = new PassiveProbeHost(resultSink, reg.Metadata.PluginId, reg.Metadata.Name);
            _running.Add(new RunningPassiveProbe(reg, plugin, host));
        }

        await RefreshAssignmentsAsync(cancellationToken);

        foreach (var rp in _running)
        {
            try
            {
                await rp.Plugin.StartAsync(rp.Host, _stopping.Token);
                rp.Started = true;
                logger.LogInformation("Started passive probe {PluginId} on {Protocol}/{Port}",
                    rp.Registration.Metadata.PluginId, rp.Plugin.Protocol, rp.Plugin.Port);
            }
            catch (Exception ex)
            {
                logger.LogError(ex, "Failed to start passive probe {PluginId}", rp.Registration.Metadata.PluginId);
            }
        }

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

        foreach (var rp in _running.Where(r => r.Started))
        {
            try { await rp.Plugin.StopAsync(cancellationToken); }
            catch (Exception ex)
            {
                logger.LogWarning(ex, "Error stopping passive probe {PluginId}", rp.Registration.Metadata.PluginId);
            }
        }
    }

    /// <summary>
    /// Pull the current specs and refresh each plugin's accumulated assignment store. Plugins read
    /// the snapshot per inbound event, so updates take effect without restarting their listeners.
    /// </summary>
    private async Task RefreshAssignmentsAsync(CancellationToken ct)
    {
        IReadOnlyList<ProbeSpec> specs;
        try
        {
            specs = await configSource.GetProbeSpecsAsync(ct);
        }
        catch (Exception ex)
        {
            logger.LogWarning(ex, "Could not refresh passive assignments; keeping last-known set");
            return;
        }

        foreach (var rp in _running)
        {
            var pluginId = rp.Registration.Metadata.PluginId;
            var assignments = specs
                .Where(s => s.Enabled && string.Equals(s.ProbePluginId, pluginId, StringComparison.Ordinal))
                .Select(s => new PassiveAssignment(s.HostId, s.HostAddress, s.AssignmentId, s.MergedConfig))
                .ToList();

            rp.Host.UpdateAssignments(assignments);
        }
    }

    private async Task RunPeriodicReconcileAsync(CancellationToken ct)
    {
        using var timer = new PeriodicTimer(ReconcileInterval);
        try
        {
            while (await timer.WaitForNextTickAsync(ct))
            {
                try { await RefreshAssignmentsAsync(ct); }
                catch (OperationCanceledException) { break; }
                catch (Exception ex) { logger.LogError(ex, "Periodic passive assignment refresh failed; will retry"); }
            }
        }
        catch (OperationCanceledException) { /* shutting down */ }
    }

    private sealed class RunningPassiveProbe(
        Mone.PluginEngine.PluginRegistration registration,
        IPassiveProbePlugin plugin,
        PassiveProbeHost host)
    {
        public Mone.PluginEngine.PluginRegistration Registration { get; } = registration;
        public IPassiveProbePlugin Plugin { get; } = plugin;
        public PassiveProbeHost Host { get; } = host;
        public bool Started { get; set; }
    }
}
