namespace Mone.Dashboard.Helpers;

// Single name-sort chokepoint for the HostDetail Assignment-tab lists. Pure/static so the
// ordering is unit-testable without a renderer, mirroring ProbeRowHelper. S09 will wrap this
// with OrderByDescending(Enabled) to surface enabled assignments first.
public static class AssignmentOrdering
{
    public static IReadOnlyList<T> ByName<T>(IEnumerable<T> items, Func<T, string> nameSelector) =>
        items.OrderBy(nameSelector, StringComparer.OrdinalIgnoreCase).ToList();

    // S09: enabled assignments first, then disabled, with S08's case-insensitive alphabetical
    // order preserved within each group. OrderByDescending(bool) puts true (enabled) ahead of
    // false; ThenBy is stable so equal keys keep input order.
    public static IReadOnlyList<T> ByNameEnabledFirst<T>(
        IEnumerable<T> items,
        Func<T, string> nameSelector,
        Func<T, bool> enabledSelector) =>
        items
            .OrderByDescending(enabledSelector)
            .ThenBy(nameSelector, StringComparer.OrdinalIgnoreCase)
            .ToList();

    public static string WithDisabledSuffix(string name, bool enabled) =>
        enabled ? name : name + " (disabled)";
}
