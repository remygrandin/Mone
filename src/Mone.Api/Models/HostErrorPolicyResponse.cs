namespace Mone.Api.Models;

public sealed record HostErrorPolicyResponse(
    Guid HostId,
    int ErrorThreshold);
