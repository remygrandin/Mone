using Microsoft.EntityFrameworkCore;
using Mone.Infrastructure.Data;

namespace Mone.Infrastructure.Services;

/// <summary>
/// Resolves the group/tag membership relationships that scoped role assignments are checked
/// against. Upward walks (host/group → ancestors) feed the authorization hot path; downward
/// walks (group → descendant groups/hosts) are used only by the IAM UI to preview a grant's reach.
/// </summary>
public sealed class ScopeResolver
{
    private readonly MoneDbContext _db;

    public ScopeResolver(MoneDbContext db)
    {
        _db = db;
    }

    /// <summary>
    /// Every group id that a host belongs to, directly or transitively (a group-scope assignment on
    /// any of these covers the host). Mirrors InheritanceResolver's ancestor walk.
    /// </summary>
    public async Task<List<Guid>> GetHostGroupClosureAsync(Guid hostId, CancellationToken ct = default)
    {
        var directGroupIds = await _db.HostGroupMemberships
            .Where(m => m.HostId == hostId)
            .Select(m => m.GroupId)
            .ToListAsync(ct);

        var all = new HashSet<Guid>();
        var queue = new Queue<Guid>(directGroupIds);

        while (queue.Count > 0)
        {
            var current = queue.Dequeue();
            if (!all.Add(current))
                continue;

            var parentId = await _db.HostGroups
                .Where(g => g.Id == current)
                .Select(g => g.ParentGroupId)
                .FirstOrDefaultAsync(ct);

            if (parentId.HasValue && !all.Contains(parentId.Value))
                queue.Enqueue(parentId.Value);
        }

        return all.ToList();
    }

    /// <summary>Tag ids carried by a host (a tag-scope assignment on any of these covers the host).</summary>
    public async Task<List<Guid>> GetHostTagIdsAsync(Guid hostId, CancellationToken ct = default)
    {
        return await _db.HostTags
            .Where(t => t.HostId == hostId)
            .Select(t => t.TagId)
            .ToListAsync(ct);
    }

    /// <summary>
    /// A group plus all of its ancestors (a group-scope assignment on any of these covers the
    /// target group). Walks up the ParentGroupId chain.
    /// </summary>
    public async Task<List<Guid>> GetGroupAncestorsAndSelfAsync(Guid groupId, CancellationToken ct = default)
    {
        var all = new HashSet<Guid>();
        var currentId = (Guid?)groupId;

        while (currentId is { } id && all.Add(id))
        {
            currentId = await _db.HostGroups
                .Where(g => g.Id == id)
                .Select(g => g.ParentGroupId)
                .FirstOrDefaultAsync(ct);
        }

        return all.ToList();
    }

    /// <summary>A group plus all of its descendant subgroups (downward BFS). IAM UI preview only.</summary>
    public async Task<List<Guid>> GetGroupSubtreeAsync(Guid groupId, CancellationToken ct = default)
    {
        var all = new HashSet<Guid>();
        var queue = new Queue<Guid>();
        queue.Enqueue(groupId);

        while (queue.Count > 0)
        {
            var current = queue.Dequeue();
            if (!all.Add(current))
                continue;

            var children = await _db.HostGroups
                .Where(g => g.ParentGroupId == current)
                .Select(g => g.Id)
                .ToListAsync(ct);

            foreach (var child in children)
                queue.Enqueue(child);
        }

        return all.ToList();
    }

    /// <summary>Distinct host ids in a group's subtree (downward). IAM UI preview only.</summary>
    public async Task<List<Guid>> GetHostsInGroupSubtreeAsync(Guid groupId, CancellationToken ct = default)
    {
        var groupIds = await GetGroupSubtreeAsync(groupId, ct);

        return await _db.HostGroupMemberships
            .Where(m => groupIds.Contains(m.GroupId))
            .Select(m => m.HostId)
            .Distinct()
            .ToListAsync(ct);
    }
}
