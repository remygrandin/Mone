namespace Mone.Api.Models;

public sealed record CreateHostGroupRequest(
    string Name,
    string? Description = null,
    Guid? ParentGroupId = null);
