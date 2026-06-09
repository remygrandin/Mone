namespace Mone.Api.Models;

public sealed record UpdateCheckerAssignmentRequest(
    string CheckerPluginId,
    string Name,
    string? ConfigJson = null,
    bool Enabled = true);
