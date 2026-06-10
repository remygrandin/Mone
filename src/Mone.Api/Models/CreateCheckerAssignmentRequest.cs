namespace Mone.Api.Models;

public sealed record CreateCheckerAssignmentRequest(
    string CheckerPluginId,
    string Name,
    string? ConfigJson = null,
    bool Enabled = true,
    Guid? ExecutorNodeId = null);
