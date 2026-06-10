using System.ComponentModel.DataAnnotations;
using Mone.Contracts.Models;

namespace Mone.Infrastructure.Data.Entities;

public sealed class ExecutorNodeEntity
{
    public Guid Id { get; set; }
    [MaxLength(256)]
    public required string Name { get; set; }
    [MaxLength(256)]
    public string? Hostname { get; set; }
    [MaxLength(64)]
    public string? Address { get; set; }
    public ExecutorRole Role { get; set; }
    [MaxLength(64)]
    public string? Version { get; set; }
    public DateTimeOffset? LastHeartbeatAt { get; set; }
    public DateTimeOffset RegisteredAt { get; set; }
}
