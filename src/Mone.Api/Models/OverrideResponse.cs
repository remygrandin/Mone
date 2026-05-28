namespace Mone.Api.Models;

public sealed class OverrideResponse
{
    public Guid Id { get; set; }
    public Guid HostId { get; set; }
    public Guid AssignmentId { get; set; }
    public string? ConfigJsonOverride { get; set; }
    public bool IsDisabled { get; set; }
    public DateTime CreatedAt { get; set; }
    public DateTime UpdatedAt { get; set; }
    public string Type { get; set; } = null!;
}
