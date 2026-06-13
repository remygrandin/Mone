using Microsoft.EntityFrameworkCore;
using Mone.Api.Authorization;
using Mone.Contracts.Models;
using Mone.Infrastructure.Data;
using Mone.Infrastructure.Services;

namespace Mone.Api.Services;

public interface IPermissionService
{
    /// <summary>
    /// The effective access level a user holds for a resource against a specific target, computed
    /// as the MAX level across every role assignment whose scope covers the target.
    /// </summary>
    Task<PermissionLevel> GetEffectiveLevelAsync(
        string userId, PermissionResource resource, ScopeTarget target, CancellationToken ct = default);

    /// <summary>
    /// The coarse per-resource capability of a user (MAX level held anywhere, ignoring scope).
    /// Used by the dashboard to gate navigation/pages; per-item actions still enforce scope.
    /// </summary>
    Task<IReadOnlyDictionary<PermissionResource, PermissionLevel>> GetCapabilitiesAsync(
        string userId, CancellationToken ct = default);
}

public sealed class PermissionService : IPermissionService
{
    private readonly MoneDbContext _db;
    private readonly ScopeResolver _scope;

    public PermissionService(MoneDbContext db, ScopeResolver scope)
    {
        _db = db;
        _scope = scope;
    }

    public async Task<PermissionLevel> GetEffectiveLevelAsync(
        string userId, PermissionResource resource, ScopeTarget target, CancellationToken ct = default)
    {
        var grants = await (
            from a in _db.UserRoleAssignments
            where a.UserId == userId
            join p in _db.RolePermissions on a.RoleId equals p.RoleId
            where p.Resource == resource && p.Level != PermissionLevel.None
            select new { a.ScopeType, a.ScopeId, p.Level })
            .ToListAsync(ct);

        if (grants.Count == 0)
            return PermissionLevel.None;

        var best = PermissionLevel.None;
        var hasScopedGrant = false;

        foreach (var g in grants)
        {
            if (g.ScopeType == ScopeType.Global)
            {
                if (g.Level > best) best = g.Level;
            }
            else
            {
                hasScopedGrant = true;
            }
        }

        // Manage is the ceiling; and global/system targets only honor Global-scope grants.
        if (best == PermissionLevel.Manage || target.Kind == ScopeTargetKind.Global || !hasScopedGrant)
            return best;

        HashSet<Guid> coveringGroups;
        HashSet<Guid> coveringTags = [];

        if (target.Kind == ScopeTargetKind.Host)
        {
            coveringGroups = [.. await _scope.GetHostGroupClosureAsync(target.Id, ct)];
            coveringTags = [.. await _scope.GetHostTagIdsAsync(target.Id, ct)];
        }
        else // Group
        {
            coveringGroups = [.. await _scope.GetGroupAncestorsAndSelfAsync(target.Id, ct)];
        }

        foreach (var g in grants)
        {
            if (g.ScopeId is not { } scopeId)
                continue;

            var covers = g.ScopeType switch
            {
                ScopeType.Group => coveringGroups.Contains(scopeId),
                ScopeType.Tag => coveringTags.Contains(scopeId),
                _ => false
            };

            if (covers && g.Level > best)
                best = g.Level;
        }

        return best;
    }

    public async Task<IReadOnlyDictionary<PermissionResource, PermissionLevel>> GetCapabilitiesAsync(
        string userId, CancellationToken ct = default)
    {
        var grants = await (
            from a in _db.UserRoleAssignments
            where a.UserId == userId
            join p in _db.RolePermissions on a.RoleId equals p.RoleId
            where p.Level != PermissionLevel.None
            select new { p.Resource, p.Level })
            .ToListAsync(ct);

        var caps = new Dictionary<PermissionResource, PermissionLevel>();
        foreach (var g in grants)
        {
            if (!caps.TryGetValue(g.Resource, out var existing) || g.Level > existing)
                caps[g.Resource] = g.Level;
        }

        return caps;
    }
}
