using Mone.Dashboard.Models;

namespace Mone.Dashboard.Theme;

// Colorblind-safe status palette (D051). Plain hex tokens, no MudBlazor dependency.
// Each MonitoringStatus carries a per-surface variant because a single hex cannot meet
// WCAG AA non-text contrast (>= 3:1) against both a near-white and a near-black surface.
// Hues derive from the Okabe-Ito CVD-safe basis (bluish-green / amber / vermillion-red /
// blue / grey); the warm Degraded/Unhealthy pair is additionally separated on lightness so
// the four operational statuses stay pairwise-distinguishable (CIE76 deltaE >= 15) under
// deuteranopia and protanopia simulation. T03's tests are the gate on these exact values.
public static class MonePalette
{
    public const string SurfaceLight = "#FAFAFA";
    public const string SurfaceDark = "#1E1E1E";

    public static IReadOnlyDictionary<MonitoringStatus, string> LightStatusColors { get; } =
        new Dictionary<MonitoringStatus, string>
        {
            [MonitoringStatus.Healthy] = "#0F7A52",
            [MonitoringStatus.Degraded] = "#B07A00",
            [MonitoringStatus.Unhealthy] = "#9E1B12",
            [MonitoringStatus.Unreachable] = "#1F5FA8",
            [MonitoringStatus.Unknown] = "#595959",
        };

    public static IReadOnlyDictionary<MonitoringStatus, string> DarkStatusColors { get; } =
        new Dictionary<MonitoringStatus, string>
        {
            [MonitoringStatus.Healthy] = "#34D399",
            [MonitoringStatus.Degraded] = "#E8B800",
            [MonitoringStatus.Unhealthy] = "#FF7A5C",
            [MonitoringStatus.Unreachable] = "#5AA9E6",
            [MonitoringStatus.Unknown] = "#A6A6A6",
        };
}
