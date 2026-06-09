namespace Mone.Dashboard.Models;

public sealed record CreateCheckerAssignmentRequest(
    string CheckerPluginId,
    string Name,
    string? ConfigJson = null,
    bool Enabled = true);

public sealed record UpdateCheckerAssignmentRequest(
    string CheckerPluginId,
    string Name,
    string? ConfigJson = null,
    bool Enabled = true);
