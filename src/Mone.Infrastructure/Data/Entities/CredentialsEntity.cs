namespace Mone.Infrastructure.Data.Entities;

public sealed class CredentialsEntity
{
    public Guid Id { get; set; }
    public required string Name { get; set; }
    public required string Username { get; set; }
    public required string Password { get; set; }
    public DateTimeOffset CreatedAt { get; set; }
    public DateTimeOffset UpdatedAt { get; set; }
}
