namespace Mone.Api.Models;

public sealed record ProbeAssignmentMetricResponse(
    Guid Id,
    Guid ProbeAssignmentId,
    string RawKey,
    string FullKey,
    string DisplayName,
    string? Unit,
    string? ValueMappingJson);

public sealed record HostDeclaredMetricResponse(
    string FullKey,
    string DisplayName,
    string? Unit,
    string? ValueMappingJson,
    Guid ProbeAssignmentId,
    string ProbeAssignmentName,
    string ProbePluginId);
