namespace Mone.Dashboard.Helpers;

// Pure row-projection decisions for the host-dashboard probe table. Mirrors the private
// ProbeNameFromPluginId / FormatRelativeTime / IsPassiveProbe logic in HostDetail.razor so
// the render choices are unit-testable without a renderer and the markup has one source of truth.
public static class ProbeRowHelper
{
    public static string ResolveDisplayName(string? customName, string probePluginId)
    {
        if (!string.IsNullOrWhiteSpace(customName))
            return customName;

        var at = probePluginId.IndexOf('@');
        return at > 0 ? probePluginId[..at] : probePluginId;
    }

    public static string FormatTriggerToast(string? customName, string probePluginId) =>
        $"Probe '{ResolveDisplayName(customName, probePluginId)}' triggered.";

    public static string FormatLastRun(DateTimeOffset? lastRun, DateTimeOffset now)
    {
        if (lastRun is null)
            return "Never";

        var elapsed = now - lastRun.Value;
        if (elapsed.TotalSeconds < 60) return "just now";
        if (elapsed.TotalMinutes < 60) return $"{(int)elapsed.TotalMinutes}m ago";
        if (elapsed.TotalHours < 24) return $"{(int)elapsed.TotalHours}h ago";
        return $"{(int)elapsed.TotalDays}d ago";
    }

    public static bool IsPassiveMode(string? probeMode) =>
        string.Equals(probeMode, "Passive", StringComparison.OrdinalIgnoreCase);
}
