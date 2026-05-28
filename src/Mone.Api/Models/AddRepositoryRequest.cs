namespace Mone.Api.Models;

public sealed record AddRepositoryRequest(
    string Owner,
    string Repo,
    string? Branch = null,
    string? DisplayName = null);
