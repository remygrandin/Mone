namespace Mone.Contracts.Models;

public sealed record ConfigField
{
    public required string Key { get; init; }
    public required string DisplayName { get; init; }
    public string? Description { get; init; }
    public required ConfigFieldType FieldType { get; init; }
    public string? DefaultValue { get; init; }
    public required bool Required { get; init; }
    public required bool IsGlobal { get; init; }
    public IReadOnlyList<string>? Choices { get; init; }
    public ConfigValidationRules? ValidationRules { get; init; }
}
