using Mone.Infrastructure.Services;

namespace Mone.Api.Models;

// Named response DTOs for non-CRUD action endpoints. Each wraps the exact same
// values the prior anonymous body carried; Web/camelCase serialization defaults
// reproduce the original lowercase JSON keys, so the wire shape is unchanged.

public sealed record EffectiveAssignmentsResponse(
    IReadOnlyList<EffectiveProbeAssignment> Probes,
    IReadOnlyList<EffectiveCheckerAssignment> Checkers);

public sealed record HostOverridesResponse(
    IReadOnlyList<OverrideResponse> Probes,
    IReadOnlyList<OverrideResponse> Checkers);

public sealed record ExecutorNodeRegistrationResponse(Guid Id);

public sealed record RepositorySyncAllResponse(int Synced);

public sealed record ProbeTriggerResponse(string Status, string ProbePluginId, Guid HostId);

public sealed record ForceStatusResponse(string Status, string CheckerPluginId, Guid HostId);
