namespace Mone.Dashboard.Models;

public record CredentialsResponse(Guid Id, string Name, string Username, DateTimeOffset CreatedAt, DateTimeOffset UpdatedAt);

public record CreateCredentialsRequest(string Name, string Username, string Password);

public record UpdateCredentialsRequest(string? Name, string? Username, string? Password);
