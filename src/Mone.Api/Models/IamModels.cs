using Mone.Contracts.Models;

namespace Mone.Api.Models;

// ---- Roles ----

public sealed record RolePermissionDto(PermissionResource Resource, PermissionLevel Level);

public sealed record RoleResponse(
    Guid Id,
    string Name,
    string? Description,
    bool IsSystem,
    IReadOnlyList<RolePermissionDto> Permissions,
    int AssignmentCount,
    DateTimeOffset CreatedAt,
    DateTimeOffset UpdatedAt);

public sealed record UpsertRoleRequest(
    string Name,
    string? Description,
    IReadOnlyList<RolePermissionDto> Permissions);

// ---- Users & assignments ----

public sealed record UserScopeDto(ScopeType ScopeType, Guid? ScopeId, string? ScopeName);

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
    IReadOnlyList<UserRoleAssignmentResponse> Assignments);

public sealed record AssignRoleRequest(Guid RoleId, ScopeType ScopeType, Guid? ScopeId);

// ---- Permission catalog & self ----

public sealed record PermissionResourceInfo(
    PermissionResource Resource,
    string Description,
    string AppliesTo,
    bool Scopeable);

public sealed record PermissionCatalogResponse(
    IReadOnlyList<PermissionResourceInfo> Resources,
    IReadOnlyList<string> Levels,
    IReadOnlyList<string> ScopeTypes);

public sealed record MyPermissionsResponse(
    string UserId,
    IReadOnlyDictionary<PermissionResource, PermissionLevel> Capabilities);
