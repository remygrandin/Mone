namespace Mone.Dashboard.Models;

public sealed record HostGroupResponse(
    Guid Id,
    string Name,
    string? Description,
    Guid? ParentGroupId,
    DateTimeOffset CreatedAt,
    DateTimeOffset UpdatedAt,
    int MemberCount,
    int ChildGroupCount);

public sealed record HostGroupDetailResponse(
    Guid Id,
    string Name,
    string? Description,
    Guid? ParentGroupId,
    DateTimeOffset CreatedAt,
    DateTimeOffset UpdatedAt,
    HostGroupMemberResponse[] Members,
    HostGroupChildResponse[] Children);

public sealed record HostGroupMemberResponse(Guid HostId, string HostName);

public sealed record HostGroupChildResponse(Guid Id, string Name);

public sealed record CreateHostGroupRequest(string Name, string? Description, Guid? ParentGroupId);

public sealed record UpdateHostGroupRequest(string Name, string? Description, Guid? ParentGroupId);

public sealed record AddGroupMemberRequest(Guid HostId);
