using Microsoft.EntityFrameworkCore;
using Mone.Infrastructure.Data;
using Mone.Infrastructure.Services;

namespace Mone.Api.Services;

public sealed class CheckerAssignmentNameValidator
{
    private readonly MoneDbContext _db;

    public CheckerAssignmentNameValidator(MoneDbContext db)
    {
        _db = db;
    }

    public async Task<NameValidationResult> ValidateForHostAsync(
        Guid hostId,
        string rawName,
        Guid? excludeAssignmentId,
        CancellationToken ct)
    {
        if (!ProbeAssignmentNaming.TryNormalize(rawName, out var name, out var snake, out var error))
            return new NameValidationResult(false, error, string.Empty, string.Empty);

        var directExists = await _db.CheckerAssignments
            .AnyAsync(c => c.HostId == hostId
                          && c.NameSnakeCase == snake
                          && (excludeAssignmentId == null || c.Id != excludeAssignmentId),
                      ct);
        if (directExists)
            return new NameValidationResult(false, $"A checker named '{snake}' already exists on this host.", name, snake);

        var groupIds = await GetAllGroupIdsForHostAsync(hostId, ct);
        if (groupIds.Count > 0)
        {
            var inheritedExists = await _db.CheckerAssignments
                .AnyAsync(c => c.GroupId != null && groupIds.Contains(c.GroupId.Value) && c.NameSnakeCase == snake, ct);
            if (inheritedExists)
                return new NameValidationResult(false, $"A checker named '{snake}' is already inherited from a group on this host.", name, snake);
        }

        return new NameValidationResult(true, null, name, snake);
    }

    public async Task<NameValidationResult> ValidateForGroupAsync(
        Guid groupId,
        string rawName,
        Guid? excludeAssignmentId,
        CancellationToken ct)
    {
        if (!ProbeAssignmentNaming.TryNormalize(rawName, out var name, out var snake, out var error))
            return new NameValidationResult(false, error, string.Empty, string.Empty);

        var groupExists = await _db.CheckerAssignments
            .AnyAsync(c => c.GroupId == groupId
                          && c.NameSnakeCase == snake
                          && (excludeAssignmentId == null || c.Id != excludeAssignmentId),
                      ct);
        if (groupExists)
            return new NameValidationResult(false, $"A checker named '{snake}' already exists in this group.", name, snake);

        var memberHostIds = await GetMemberHostIdsTransitiveAsync(groupId, ct);
        if (memberHostIds.Count > 0)
        {
            var directConflict = await _db.CheckerAssignments
                .AnyAsync(c => c.HostId != null && memberHostIds.Contains(c.HostId.Value) && c.NameSnakeCase == snake, ct);
            if (directConflict)
                return new NameValidationResult(false, $"A member host already has a direct checker named '{snake}'.", name, snake);

            var siblingGroupIds = await _db.HostGroupMemberships
                .Where(m => memberHostIds.Contains(m.HostId))
                .Select(m => m.GroupId)
                .Distinct()
                .Where(g => g != groupId)
                .ToListAsync(ct);

            var allSiblingsAndAncestors = new HashSet<Guid>(siblingGroupIds);
            foreach (var sg in siblingGroupIds.ToList())
            {
                var ancestors = await GetAncestorsAsync(sg, ct);
                foreach (var a in ancestors) allSiblingsAndAncestors.Add(a);
            }
            allSiblingsAndAncestors.Remove(groupId);

            if (allSiblingsAndAncestors.Count > 0)
            {
                var otherInherit = await _db.CheckerAssignments
                    .AnyAsync(c => c.GroupId != null && allSiblingsAndAncestors.Contains(c.GroupId.Value) && c.NameSnakeCase == snake, ct);
                if (otherInherit)
                    return new NameValidationResult(false, $"A sibling group inheritance already provides a checker named '{snake}' to a member host.", name, snake);
            }
        }

        return new NameValidationResult(true, null, name, snake);
    }

    public async Task<string?> ValidateMembershipAddAsync(Guid groupId, Guid hostId, CancellationToken ct)
    {
        var groupChainIds = await GetGroupAndAncestorsAsync(groupId, ct);
        var groupSnakeNames = await _db.CheckerAssignments
            .Where(c => c.GroupId != null && groupChainIds.Contains(c.GroupId.Value))
            .Select(c => c.NameSnakeCase)
            .ToListAsync(ct);

        if (groupSnakeNames.Count == 0)
            return null;

        var directConflict = await _db.CheckerAssignments
            .Where(c => c.HostId == hostId && groupSnakeNames.Contains(c.NameSnakeCase))
            .Select(c => c.NameSnakeCase)
            .FirstOrDefaultAsync(ct);
        if (directConflict is not null)
            return $"Host already has a direct checker named '{directConflict}' that would conflict with one inherited from this group.";

        var existingGroupIds = await GetAllGroupIdsForHostAsync(hostId, ct);
        if (existingGroupIds.Count > 0)
        {
            var siblingConflict = await _db.CheckerAssignments
                .Where(c => c.GroupId != null && existingGroupIds.Contains(c.GroupId.Value) && groupSnakeNames.Contains(c.NameSnakeCase))
                .Select(c => c.NameSnakeCase)
                .FirstOrDefaultAsync(ct);
            if (siblingConflict is not null)
                return $"Host already inherits a checker named '{siblingConflict}' from another group that would conflict with this group.";
        }

        return null;
    }

    private async Task<List<Guid>> GetAllGroupIdsForHostAsync(Guid hostId, CancellationToken ct)
    {
        var directGroupIds = await _db.HostGroupMemberships
            .Where(m => m.HostId == hostId)
            .Select(m => m.GroupId)
            .ToListAsync(ct);

        var result = new HashSet<Guid>();
        foreach (var gid in directGroupIds)
        {
            foreach (var a in await GetGroupAndAncestorsAsync(gid, ct))
                result.Add(a);
        }
        return result.ToList();
    }

    private async Task<List<Guid>> GetGroupAndAncestorsAsync(Guid groupId, CancellationToken ct)
    {
        var result = new HashSet<Guid> { groupId };
        var current = groupId;
        while (true)
        {
            var parent = await _db.HostGroups
                .Where(g => g.Id == current)
                .Select(g => g.ParentGroupId)
                .FirstOrDefaultAsync(ct);
            if (parent is null || !result.Add(parent.Value))
                break;
            current = parent.Value;
        }
        return result.ToList();
    }

    private async Task<List<Guid>> GetAncestorsAsync(Guid groupId, CancellationToken ct)
    {
        var result = new List<Guid>();
        var current = groupId;
        var seen = new HashSet<Guid> { current };
        while (true)
        {
            var parent = await _db.HostGroups
                .Where(g => g.Id == current)
                .Select(g => g.ParentGroupId)
                .FirstOrDefaultAsync(ct);
            if (parent is null || !seen.Add(parent.Value))
                break;
            result.Add(parent.Value);
            current = parent.Value;
        }
        return result;
    }

    private async Task<List<Guid>> GetMemberHostIdsTransitiveAsync(Guid groupId, CancellationToken ct)
    {
        var groupAndDescendants = new HashSet<Guid> { groupId };
        var queue = new Queue<Guid>();
        queue.Enqueue(groupId);
        while (queue.Count > 0)
        {
            var current = queue.Dequeue();
            var children = await _db.HostGroups
                .Where(g => g.ParentGroupId == current)
                .Select(g => g.Id)
                .ToListAsync(ct);
            foreach (var c in children)
            {
                if (groupAndDescendants.Add(c))
                    queue.Enqueue(c);
            }
        }

        return await _db.HostGroupMemberships
            .Where(m => groupAndDescendants.Contains(m.GroupId))
            .Select(m => m.HostId)
            .Distinct()
            .ToListAsync(ct);
    }
}
