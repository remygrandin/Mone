using System.Text.Json.Serialization;

namespace Mone.Contracts.Models;

/// <summary>
/// The catalog of resources that an access grant can apply to. Each resource maps to a
/// family of API endpoints (see docs/authorization.md for the authoritative mapping).
/// </summary>
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

/// <summary>
/// Access level for a resource. Ordered so that the effective level can be computed as a MAX
/// across all assignments that cover a target.
/// </summary>
[JsonConverter(typeof(JsonStringEnumConverter))]
public enum PermissionLevel
{
    None = 0,
    View = 1,
    Manage = 2
}

/// <summary>
/// The breadth of a role assignment. Global applies everywhere; Tag covers hosts carrying a
/// tag; Group covers a group, its descendant subgroups, and all hosts in that subtree.
/// </summary>
[JsonConverter(typeof(JsonStringEnumConverter))]
public enum ScopeType
{
    Global,
    Tag,
    Group
}
