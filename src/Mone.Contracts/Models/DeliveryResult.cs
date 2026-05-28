namespace Mone.Contracts.Models;

public sealed record DeliveryResult(
    bool Success,
    string? ErrorMessage = null,
    TimeSpan? RetryAfter = null);
