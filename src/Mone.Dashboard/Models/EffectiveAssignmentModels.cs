namespace Mone.Dashboard.Models;

public sealed record EffectiveProbeAssignment(
    Guid AssignmentId,
    string ProbePluginId,
    string ScheduleCron,
    string? ConfigJson,
    string? BackoffOverrideJson,
    bool Enabled,
    string? TargetAddressOverride,
    string SourceType,
    Guid? SourceGroupId,
    bool IsOverridden,
    string? OverrideConfigJson);

public sealed record EffectiveCheckerAssignment(
    Guid AssignmentId,
    string CheckerPluginId,
    string? ConfigJson,
    bool Enabled,
    string SourceType,
    Guid? SourceGroupId,
    bool IsOverridden,
    string? OverrideConfigJson);

public sealed record EffectiveAssignmentsResponse(
    EffectiveProbeAssignment[] Probes,
    EffectiveCheckerAssignment[] Checkers);
