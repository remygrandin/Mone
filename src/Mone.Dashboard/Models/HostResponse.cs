namespace Mone.Dashboard.Models;

public sealed record HostResponse(
    Guid Id,
    string Name,
    string Address,
    bool Enabled,
    DateTimeOffset CreatedAt,
    DateTimeOffset UpdatedAt,
    TagResponse[] Tags);
