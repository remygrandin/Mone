namespace Mone.Contracts.Models;

public sealed record ConfigValidationRules
{
    public double? Min { get; init; }
    public double? Max { get; init; }
    public string? Pattern { get; init; }
}
