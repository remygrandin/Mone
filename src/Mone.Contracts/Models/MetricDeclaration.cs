namespace Mone.Contracts.Models;

public sealed record MetricDeclaration(
    string Key,
    string DisplayName,
    string? Unit = null,
    IReadOnlyDictionary<double, string>? ValueMapping = null);
