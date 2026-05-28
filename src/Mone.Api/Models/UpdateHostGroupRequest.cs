namespace Mone.Api.Models;

public sealed record UpdateHostGroupRequest(
    string Name,
    string? Description = null,
    Guid? ParentGroupId = null);
