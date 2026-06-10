namespace Mone.Contracts.Models;

/// <summary>
/// Fully-resolved probe schedule entry served by the console to a remote executor.
/// Inheritance resolution and config merge are done server-side, so the executor can
/// schedule and run probes without a database or the inheritance resolver.
/// </summary>
public sealed record ProbeSpec(
    Guid HostId,
    string HostAddress,
    Guid AssignmentId,
    string ProbePluginId,
    string ScheduleCron,
    IReadOnlyDictionary<string, string> MergedConfig,
    string? TargetAddressOverride,
    string? NameSnakeCase,
    Guid? ExecutorNodeId,
    bool Enabled);
