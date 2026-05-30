namespace Mone.Dashboard.Models;

public sealed record DbSizeReport(
    long TotalBytes,
    string TotalPretty,
    IReadOnlyList<TableSize> Tables);

public sealed record TableSize(
    string Name,
    long Bytes,
    string Pretty);

public sealed record HousekeepingCategory(
    string Key,
    string Label,
    string Group,
    long Count,
    long? EstimatedBytes,
    string? EstimatedPretty);

public sealed record HousekeepingReport(
    DbSizeReport DbSize,
    IReadOnlyList<HousekeepingCategory> Categories);

public sealed record CleanupRequest(string Key);

public sealed record CleanupResult(string Key, long DeletedRows);
