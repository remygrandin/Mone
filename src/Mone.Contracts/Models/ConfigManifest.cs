namespace Mone.Contracts.Models;

public sealed record ConfigManifest
{
    public required IReadOnlyList<ConfigField> Fields { get; init; }
}
