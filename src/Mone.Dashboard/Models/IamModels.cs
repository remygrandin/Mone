using System.Text.Json.Serialization;

namespace Mone.Dashboard.Models;

[JsonConverter(typeof(JsonStringEnumConverter))]
public enum PermissionResource
{
    Hosts,
    Monitoring,
    Assignments,
    Groups,
    Tags,
    Notifications,
    Plugins,
    ExecutorNodes,
    System,
    Administration
}

[JsonConverter(typeof(JsonStringEnumConverter))]
public enum PermissionLevel
{
    None = 0,
    View = 1,
    Manage = 2
}

[JsonConverter(typeof(JsonStringEnumConverter))]
public enum ScopeType
{
    Global,
    Tag,
    Group
}

public sealed record RolePermissionDto(PermissionResource Resource, PermissionLevel Level);

public sealed record RoleResponse(
    Guid Id,
    string Name,
    string? Description,
    bool IsSystem,
    List<RolePermissionDto> Permissions,
    int AssignmentCount,
    DateTimeOffset CreatedAt,
    DateTimeOffset UpdatedAt);

public sealed record UpsertRoleRequest(
    string Name,
    string? Description,
    List<RolePermissionDto> Permissions);

public sealed record UserRoleAssignmentResponse(
    Guid Id,
    Guid RoleId,
    string RoleName,
    ScopeType ScopeType,
    Guid? ScopeId,
    string? ScopeName,
    DateTimeOffset CreatedAt);

public sealed record UserWithRolesResponse(
    string Id,
    string Email,
    List<UserRoleAssignmentResponse> Assignments);

public sealed record AssignRoleRequest(Guid RoleId, ScopeType ScopeType, Guid? ScopeId);

public sealed record PermissionResourceInfo(
    PermissionResource Resource,
    string Description,
    string AppliesTo,
    bool Scopeable);

public sealed record PermissionCatalogResponse(
    List<PermissionResourceInfo> Resources,
    List<string> Levels,
    List<string> ScopeTypes);

public sealed record MyPermissionsResponse(
    string UserId,
    Dictionary<PermissionResource, PermissionLevel> Capabilities);
