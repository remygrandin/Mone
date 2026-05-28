using System.ComponentModel.DataAnnotations;

namespace Mone.Infrastructure.Data.Entities;

public sealed class ProbeAssignmentEntity
{
    public Guid Id { get; set; }
    public Guid? HostId { get; set; }
    public HostEntity? Host { get; set; }
    public Guid? GroupId { get; set; }
    public GroupEntity? Group { get; set; }
    public required string ProbePluginId { get; set; }
    public required string ScheduleCron { get; set; }
    public string? ConfigJson { get; set; }
    public string? BackoffOverrideJson { get; set; }
    [MaxLength(512)]
    public string? TargetAddressOverride { get; set; }
    public bool Enabled { get; set; } = true;
}
