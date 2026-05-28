namespace Mone.Api.Models;

public sealed record PluginRepositoryResponse(
    Guid Id,
    string Owner,
    string Repo,
    string? Branch,
    string DisplayName,
    bool Enabled,
    DateTime? LastSyncedAt,
    string? LastSyncError,
    DateTime CreatedAt);
