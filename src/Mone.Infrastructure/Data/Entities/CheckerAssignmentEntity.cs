using System.ComponentModel.DataAnnotations;

namespace Mone.Infrastructure.Data.Entities;

public sealed class CheckerAssignmentEntity
{
    public Guid Id { get; set; }
    public Guid? HostId { get; set; }
    public HostEntity? Host { get; set; }
    public Guid? GroupId { get; set; }
    public GroupEntity? Group { get; set; }
    public required string CheckerPluginId { get; set; }
    public string? ConfigJson { get; set; }
    public bool Enabled { get; set; } = true;
    [MaxLength(128)]
    public required string Name { get; set; }
    [MaxLength(128)]
    public required string NameSnakeCase { get; set; }
}
