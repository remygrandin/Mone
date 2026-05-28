using Mone.Contracts.Models;

namespace Mone.Contracts.Plugins.Attributes;

[AttributeUsage(AttributeTargets.Class, Inherited = false)]
public sealed class CheckerPluginAttribute : Attribute
{
    public required CheckerMode CheckerMode { get; init; }
}
