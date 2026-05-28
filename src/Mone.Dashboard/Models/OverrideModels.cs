namespace Mone.Dashboard.Models;

public sealed record OverrideResponse(
    Guid Id,
    Guid HostId,
    Guid AssignmentId,
    string? ConfigJsonOverride,
    bool IsDisabled,
    DateTime CreatedAt,
    DateTime UpdatedAt,
    string Type);

public sealed record UpsertOverrideRequest(
    string? ConfigJsonOverride,
    bool IsDisabled);

public sealed record OverridesResponse(
    OverrideResponse[] Probes,
    OverrideResponse[] Checkers);
