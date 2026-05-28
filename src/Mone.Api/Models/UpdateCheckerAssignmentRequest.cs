namespace Mone.Api.Models;

public sealed record UpdateCheckerAssignmentRequest(
    string CheckerPluginId,
    string? ConfigJson = null,
    bool Enabled = true);
