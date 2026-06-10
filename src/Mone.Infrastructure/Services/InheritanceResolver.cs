using System.Text.Json;
using System.Text.Json.Nodes;
using System.Text.Json.Serialization;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using Mone.Infrastructure.Data;
using Mone.Infrastructure.Data.Entities;

namespace Mone.Infrastructure.Services;

[JsonConverter(typeof(JsonStringEnumConverter))]
public enum AssignmentSourceType { Direct, Inherited }

public sealed record EffectiveProbeAssignment(
    Guid AssignmentId,
    string ProbePluginId,
    string ScheduleCron,
    string? ConfigJson,
    string? BackoffOverrideJson,
    bool Enabled,
    string? TargetAddressOverride,
    AssignmentSourceType SourceType,
    Guid? SourceGroupId,
    Guid? ExecutorNodeId = null,
    bool IsOverridden = false,
    string? OverrideConfigJson = null);

public sealed record EffectiveCheckerAssignment(
    Guid AssignmentId,
    string CheckerPluginId,
    string? ConfigJson,
    bool Enabled,
    AssignmentSourceType SourceType,
    Guid? SourceGroupId,
    Guid? ExecutorNodeId = null,
    bool IsOverridden = false,
    string? OverrideConfigJson = null);

public sealed class InheritanceResolver
{
    private readonly MoneDbContext _db;
    private readonly ILogger<InheritanceResolver> _logger;

    public InheritanceResolver(MoneDbContext db, ILogger<InheritanceResolver> logger)
    {
        _db = db;
        _logger = logger;
    }

    public async Task<List<EffectiveProbeAssignment>> GetEffectiveProbeAssignmentsAsync(Guid hostId)
    {
        var result = new List<EffectiveProbeAssignment>();

        var direct = await _db.ProbeAssignments
            .Where(a => a.HostId == hostId)
            .ToListAsync();

        foreach (var a in direct)
        {
            result.Add(new EffectiveProbeAssignment(
                a.Id, a.ProbePluginId, a.ScheduleCron, a.ConfigJson,
                a.BackoffOverrideJson, a.Enabled, a.TargetAddressOverride,
                AssignmentSourceType.Direct, null, a.ExecutorNodeId));
        }

        _logger.LogDebug("Host {HostId}: {Count} direct probe assignments", hostId, direct.Count);

        var groupIds = await GetAllGroupIdsForHostAsync(hostId);

        foreach (var groupId in groupIds)
        {
            var groupAssignments = await _db.ProbeAssignments
                .Where(a => a.GroupId == groupId)
                .ToListAsync();

            foreach (var a in groupAssignments)
            {
                result.Add(new EffectiveProbeAssignment(
                    a.Id, a.ProbePluginId, a.ScheduleCron, a.ConfigJson,
                    a.BackoffOverrideJson, a.Enabled, a.TargetAddressOverride,
                    AssignmentSourceType.Inherited, groupId, a.ExecutorNodeId));
            }

            _logger.LogDebug("Host {HostId}: {Count} probe assignments inherited from group {GroupId}",
                hostId, groupAssignments.Count, groupId);
        }

        result = await ApplyProbeOverridesAsync(hostId, result);

        return result;
    }

    public async Task<List<EffectiveCheckerAssignment>> GetEffectiveCheckerAssignmentsAsync(Guid hostId)
    {
        var result = new List<EffectiveCheckerAssignment>();

        var direct = await _db.CheckerAssignments
            .Where(a => a.HostId == hostId)
            .ToListAsync();

        foreach (var a in direct)
        {
            result.Add(new EffectiveCheckerAssignment(
                a.Id, a.CheckerPluginId, a.ConfigJson, a.Enabled,
                AssignmentSourceType.Direct, null, a.ExecutorNodeId));
        }

        _logger.LogDebug("Host {HostId}: {Count} direct checker assignments", hostId, direct.Count);

        var groupIds = await GetAllGroupIdsForHostAsync(hostId);

        foreach (var groupId in groupIds)
        {
            var groupAssignments = await _db.CheckerAssignments
                .Where(a => a.GroupId == groupId)
                .ToListAsync();

            foreach (var a in groupAssignments)
            {
                result.Add(new EffectiveCheckerAssignment(
                    a.Id, a.CheckerPluginId, a.ConfigJson, a.Enabled,
                    AssignmentSourceType.Inherited, groupId, a.ExecutorNodeId));
            }

            _logger.LogDebug("Host {HostId}: {Count} checker assignments inherited from group {GroupId}",
                hostId, groupAssignments.Count, groupId);
        }

        result = await ApplyCheckerOverridesAsync(hostId, result);

        return result;
    }

    private async Task<List<EffectiveProbeAssignment>> ApplyProbeOverridesAsync(
        Guid hostId, List<EffectiveProbeAssignment> assignments)
    {
        var overrides = await _db.ProbeAssignmentOverrides
            .Where(o => o.HostId == hostId)
            .ToListAsync();

        if (overrides.Count == 0) return assignments;

        var overridesByAssignment = overrides.ToDictionary(o => o.ProbeAssignmentId);
        var result = new List<EffectiveProbeAssignment>(assignments.Count);

        foreach (var a in assignments)
        {
            if (!overridesByAssignment.TryGetValue(a.AssignmentId, out var ov))
            {
                result.Add(a);
                continue;
            }

            if (ov.IsDisabled)
            {
                _logger.LogDebug("Host {HostId}: probe assignment {AssignmentId} disabled by override {OverrideId}",
                    hostId, a.AssignmentId, ov.Id);
                continue;
            }

            var mergedConfig = MergeConfigJson(a.ConfigJson, ov.ConfigJsonOverride);
            _logger.LogDebug("Host {HostId}: probe assignment {AssignmentId} config overridden by {OverrideId}",
                hostId, a.AssignmentId, ov.Id);

            result.Add(a with
            {
                ConfigJson = mergedConfig,
                IsOverridden = true,
                OverrideConfigJson = ov.ConfigJsonOverride
            });
        }

        return result;
    }

    private async Task<List<EffectiveCheckerAssignment>> ApplyCheckerOverridesAsync(
        Guid hostId, List<EffectiveCheckerAssignment> assignments)
    {
        var overrides = await _db.CheckerAssignmentOverrides
            .Where(o => o.HostId == hostId)
            .ToListAsync();

        if (overrides.Count == 0) return assignments;

        var overridesByAssignment = overrides.ToDictionary(o => o.CheckerAssignmentId);
        var result = new List<EffectiveCheckerAssignment>(assignments.Count);

        foreach (var a in assignments)
        {
            if (!overridesByAssignment.TryGetValue(a.AssignmentId, out var ov))
            {
                result.Add(a);
                continue;
            }

            if (ov.IsDisabled)
            {
                _logger.LogDebug("Host {HostId}: checker assignment {AssignmentId} disabled by override {OverrideId}",
                    hostId, a.AssignmentId, ov.Id);
                continue;
            }

            var mergedConfig = MergeConfigJson(a.ConfigJson, ov.ConfigJsonOverride);
            _logger.LogDebug("Host {HostId}: checker assignment {AssignmentId} config overridden by {OverrideId}",
                hostId, a.AssignmentId, ov.Id);

            result.Add(a with
            {
                ConfigJson = mergedConfig,
                IsOverridden = true,
                OverrideConfigJson = ov.ConfigJsonOverride
            });
        }

        return result;
    }

    internal static string? MergeConfigJson(string? baseJson, string? overrideJson)
    {
        if (string.IsNullOrEmpty(overrideJson)) return baseJson;
        if (string.IsNullOrEmpty(baseJson)) return overrideJson;

        var baseNode = JsonNode.Parse(baseJson)?.AsObject();
        var overrideNode = JsonNode.Parse(overrideJson)?.AsObject();

        if (baseNode is null) return overrideJson;
        if (overrideNode is null) return baseJson;

        foreach (var prop in overrideNode)
        {
            baseNode[prop.Key] = prop.Value?.DeepClone();
        }

        return baseNode.ToJsonString();
    }

    private async Task<List<Guid>> GetAllGroupIdsForHostAsync(Guid hostId)
    {
        var directGroupIds = await _db.HostGroupMemberships
            .Where(m => m.HostId == hostId)
            .Select(m => m.GroupId)
            .ToListAsync();

        var allGroupIds = new HashSet<Guid>();
        var queue = new Queue<Guid>(directGroupIds);

        while (queue.Count > 0)
        {
            var current = queue.Dequeue();
            if (!allGroupIds.Add(current))
                continue;

            var parentId = await _db.HostGroups
                .Where(g => g.Id == current)
                .Select(g => g.ParentGroupId)
                .FirstOrDefaultAsync();

            if (parentId.HasValue && !allGroupIds.Contains(parentId.Value))
                queue.Enqueue(parentId.Value);
        }

        return allGroupIds.ToList();
    }
}
