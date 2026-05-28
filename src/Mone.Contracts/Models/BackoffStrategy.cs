namespace Mone.Contracts.Models;

public sealed record BackoffStrategy(
    TimeSpan InitialDelay,
    TimeSpan MaxDelay,
    double Multiplier = 2.0,
    int MaxRetries = 3);
