namespace Mone.Dashboard.Models;

public sealed record UpdateHostRequest(
    string Name,
    string Address,
    bool Enabled,
    Guid[]? TagIds = null);
