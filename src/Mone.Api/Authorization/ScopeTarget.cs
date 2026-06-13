namespace Mone.Api.Authorization;

public enum ScopeTargetKind
{
    /// <summary>A global/system resource with no per-entity scope (e.g. Tags, Plugins, System).</summary>
    Global,
    Host,
    Group
}

/// <summary>The entity an authorization check is evaluated against.</summary>
public readonly record struct ScopeTarget(ScopeTargetKind Kind, Guid Id)
{
    public static readonly ScopeTarget Global = new(ScopeTargetKind.Global, Guid.Empty);
    public static ScopeTarget Host(Guid id) => new(ScopeTargetKind.Host, id);
    public static ScopeTarget Group(Guid id) => new(ScopeTargetKind.Group, id);
}
