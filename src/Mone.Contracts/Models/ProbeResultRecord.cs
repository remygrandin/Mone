namespace Mone.Contracts.Models;

public sealed record ProbeResultRecord(
    DateTimeOffset Timestamp,
    string TargetId,
    string ProbeId,
    ProbeResult Result);
