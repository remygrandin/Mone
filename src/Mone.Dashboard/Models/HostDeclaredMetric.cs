namespace Mone.Dashboard.Models;

public sealed record HostDeclaredMetric(
    string FullKey,
    string DisplayName,
    string? Unit,
    string? ValueMappingJson,
    Guid ProbeAssignmentId,
    string ProbeAssignmentName,
    string ProbePluginId);
