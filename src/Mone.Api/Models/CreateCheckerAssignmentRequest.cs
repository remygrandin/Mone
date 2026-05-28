namespace Mone.Api.Models;

public sealed record CreateCheckerAssignmentRequest(
    string CheckerPluginId,
    string? ConfigJson = null,
    bool Enabled = true);
