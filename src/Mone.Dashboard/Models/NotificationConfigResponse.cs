namespace Mone.Dashboard.Models;

public sealed record NotificationConfigResponse(
    Guid Id,
    string PluginId,
    string? ConfigJson,
    bool Enabled,
    string? Scope);
