namespace Mone.Dashboard.Models;

public sealed record CreateHostRequest(
    string Name,
    string Address,
    bool Enabled = true,
    Guid[]? TagIds = null);
