namespace Mone.Api.Models;

public sealed class UpsertOverrideRequest
{
    public string? ConfigJsonOverride { get; set; }
    public bool IsDisabled { get; set; }
}
