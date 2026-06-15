using Mone.Dashboard.Models;
using Mone.Dashboard.Theme;
using Mone.Dashboard.Tests.Support;
using Xunit;

namespace Mone.Dashboard.Tests;

// The objective gate for S01 (R032 / D053): proves MonePalette is WCAG AA non-text
// contrast (>= 3:1) on its own surface and that the four operational statuses stay
// pairwise-distinguishable (CIE76 deltaE >= 15) under deuteranopia and protanopia on
// both surfaces. Browserless: ColorMath (T02) measures MonePalette (T01) directly.
// Every assertion message embeds token name(s), surface, CVD model, and the measured
// value so a failure self-localizes to the exact token/surface/CVD that fell short.
public class PaletteAccessibilityTests
{
    private const double MinContrast = 3.0;       // WCAG AA non-text contrast.
    private const double MinDistinguishDeltaE = 15.0; // Pairwise CIE76 separation floor.

    // The four operational statuses that must stay pairwise-distinguishable. Unknown
    // (grey) is excluded here — it is trivially distinct from the chromatic four — but
    // is still exercised by the contrast theory below.
    private static readonly MonitoringStatus[] OperationalStatuses =
    {
        MonitoringStatus.Healthy,
        MonitoringStatus.Degraded,
        MonitoringStatus.Unhealthy,
        MonitoringStatus.Unreachable,
    };

    public static IEnumerable<object[]> ContrastCases()
    {
        foreach (var status in Enum.GetValues<MonitoringStatus>())
        {
            yield return new object[] { status, "Light" };
            yield return new object[] { status, "Dark" };
        }
    }

    [Theory]
    [MemberData(nameof(ContrastCases))]
    public void StatusToken_MeetsWcagAaNonTextContrast_AgainstOwnSurface(MonitoringStatus status, string surface)
    {
        (string token, string surfaceColor) = surface == "Light"
            ? (MonePalette.LightStatusColors[status], MonePalette.SurfaceLight)
            : (MonePalette.DarkStatusColors[status], MonePalette.SurfaceDark);

        double ratio = ColorMath.ContrastRatio(token, surfaceColor);

        Assert.True(
            ratio >= MinContrast,
            $"{status} ({surface}) token {token} vs surface {surfaceColor} contrast {ratio:0.000} " +
            $"< WCAG AA non-text minimum {MinContrast:0.0}");
    }

    public static IEnumerable<object[]> DistinguishabilityCases()
    {
        foreach (var surface in new[] { "Light", "Dark" })
        {
            yield return new object[] { surface, "Deuteranopia" };
            yield return new object[] { surface, "Protanopia" };
        }
    }

    [Theory]
    [MemberData(nameof(DistinguishabilityCases))]
    public void OperationalStatuses_StayPairwiseDistinguishable_UnderCvd(string surface, string cvd)
    {
        var palette = surface == "Light" ? MonePalette.LightStatusColors : MonePalette.DarkStatusColors;
        Func<string, string> simulate = cvd == "Deuteranopia"
            ? ColorMath.SimulateDeuteranopia
            : ColorMath.SimulateProtanopia;

        // Simulate each operational token through the CVD model once, then compare.
        var simulated = OperationalStatuses.ToDictionary(s => s, s => simulate(palette[s]));

        for (int i = 0; i < OperationalStatuses.Length; i++)
        {
            for (int j = i + 1; j < OperationalStatuses.Length; j++)
            {
                var a = OperationalStatuses[i];
                var b = OperationalStatuses[j];
                double deltaE = ColorMath.DeltaE76(simulated[a], simulated[b]);

                Assert.True(
                    deltaE >= MinDistinguishDeltaE,
                    $"{a} vs {b} ({surface}, {cvd}) simulated {simulated[a]}/{simulated[b]} " +
                    $"deltaE76 {deltaE:0.000} < distinguishability minimum {MinDistinguishDeltaE:0.0}");
            }
        }
    }
}
