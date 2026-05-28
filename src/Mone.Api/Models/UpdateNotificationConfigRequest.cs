namespace Mone.Api.Models;

public sealed record UpdateNotificationConfigRequest(
    string PluginId,
    string? ConfigJson = null,
    bool Enabled = true,
    string? Scope = null);
