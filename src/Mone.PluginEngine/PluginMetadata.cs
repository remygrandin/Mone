using Mone.Contracts.Models;

namespace Mone.PluginEngine;

public sealed record PluginMetadata
{
    public required string PluginId { get; init; }
    public required string Name { get; init; }
    public required Version Version { get; init; }
    public required string Description { get; init; }
    public required string PluginTypeName { get; init; }
    public required PluginKind Kind { get; init; }
    public ProbeMode? ProbeMode { get; init; }
    public InstantiationMode? InstantiationMode { get; init; }
    public CheckerMode? CheckerMode { get; init; }
    public required string AssemblyPath { get; init; }
    public ConfigManifest? ConfigManifest { get; init; }
}

public enum PluginKind
{
    Probe,
    Checker,
    Notification
}
