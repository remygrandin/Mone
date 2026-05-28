namespace Mone.Api.Models;

public sealed record CreateNotificationConfigRequest(
    string PluginId,
    string? ConfigJson = null,
    bool Enabled = true,
    string? Scope = null);
