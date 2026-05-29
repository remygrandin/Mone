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

public enum CheckerInvocationMode
{
    OnProbeResult,
    OnInterval
}
