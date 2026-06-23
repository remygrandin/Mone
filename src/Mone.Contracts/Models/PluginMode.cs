namespace Mone.Contracts.Models;

public enum ProbeMode
{
    Active,
    Passive
}

public enum InstantiationMode
{
    PerTarget,
    Batch
}

[Flags]
public enum CheckerInvocationMode
{
    OnProbeResult = 1,
    OnInterval = 2,
    OnLogEvent = 4
}
