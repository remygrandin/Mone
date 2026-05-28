namespace Mone.Dashboard.Models;

public sealed record LoadedPluginModel(
    string PluginId,
    string Name,
    string Version,
    string Description,
    string Kind,
    string? ProbeMode,
    ConfigManifestModel? ConfigManifest);

public sealed record ConfigManifestModel(
    IReadOnlyList<ConfigFieldModel> Fields);

public sealed record ConfigFieldModel(
    string Key,
    string DisplayName,
    string? Description,
    ConfigFieldTypeEnum FieldType,
    string? DefaultValue,
    bool Required,
    bool IsGlobal,
    IReadOnlyList<string>? Choices,
    ConfigValidationRulesModel? ValidationRules);

public sealed record ConfigValidationRulesModel(
    double? Min,
    double? Max,
    string? Pattern);

public enum ConfigFieldTypeEnum
{
    String,
    Integer,
    Double,
    Boolean,
    Choice,
    Secret
}
