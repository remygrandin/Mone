using Mone.Contracts.Models;

namespace Mone.Contracts.Plugins.Attributes;

[AttributeUsage(AttributeTargets.Class, Inherited = false)]
public sealed class ProbePluginAttribute : Attribute
{
    public required ProbeMode ProbeMode { get; init; }
    public required InstantiationMode InstantiationMode { get; init; }
}
