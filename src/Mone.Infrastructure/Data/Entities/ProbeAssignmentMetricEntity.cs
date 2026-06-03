using System.ComponentModel.DataAnnotations;

namespace Mone.Infrastructure.Data.Entities;

public sealed class ProbeAssignmentMetricEntity
{
    public Guid Id { get; set; }
    public Guid ProbeAssignmentId { get; set; }
    public ProbeAssignmentEntity? ProbeAssignment { get; set; }
    [MaxLength(256)]
    public required string RawKey { get; set; }
    [MaxLength(384)]
    public required string FullKey { get; set; }
    [MaxLength(256)]
    public required string DisplayName { get; set; }
    [MaxLength(64)]
    public string? Unit { get; set; }
    public string? ValueMappingJson { get; set; }
}
