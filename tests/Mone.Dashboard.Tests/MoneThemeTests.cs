using Mone.Dashboard.Models;
using Mone.Dashboard.Theme;
using MudBlazor;
using MudBlazor.Utilities;
using Xunit;

namespace Mone.Dashboard.Tests;

// Proves MoneTheme binds each operational status to the SAME semantic slot StatusColorHelper
// uses (Success=Healthy, Warning=Degraded, Error=Unhealthy, Dark=Unreachable) and that the
// per-surface palette draws its hex from the matching MonePalette dictionary. Comparison is
// via MudColor.Value to be normalization-safe (case/format independent).
public class MoneThemeTests
{
    private static readonly (MonitoringStatus Status, Func<Palette, MudColor> Slot)[] StatusSlots =
    {
        (MonitoringStatus.Healthy, p => p.Success),
        (MonitoringStatus.Degraded, p => p.Warning),
        (MonitoringStatus.Unhealthy, p => p.Error),
        (MonitoringStatus.Unreachable, p => p.Dark),
    };

    public static IEnumerable<object[]> StatusSlotCases()
    {
        foreach (var (status, _) in StatusSlots)
        {
            yield return new object[] { status, "Light" };
            yield return new object[] { status, "Dark" };
        }
    }

    [Theory]
    [MemberData(nameof(StatusSlotCases))]
    public void Palette_StatusSlot_MatchesMonePaletteToken(MonitoringStatus status, string surface)
    {
        Func<Palette, MudColor> slot = Array.Find(StatusSlots, s => s.Status == status).Slot;

        (Palette palette, string expectedHex) = surface == "Light"
            ? (MoneTheme.Instance.PaletteLight, MonePalette.LightStatusColors[status])
            : ((Palette)MoneTheme.Instance.PaletteDark, MonePalette.DarkStatusColors[status]);

        string expected = new MudColor(expectedHex).Value;
        string actual = slot(palette).Value;

        Assert.True(
            expected == actual,
            $"{status} ({surface}) slot {actual} != MonePalette token {expected}");
    }

    [Fact]
    public void PaletteLight_Surface_MatchesMonePaletteSurfaceLight()
    {
        Assert.Equal(new MudColor(MonePalette.SurfaceLight).Value, MoneTheme.Instance.PaletteLight.Surface.Value);
    }

    [Fact]
    public void PaletteDark_Surface_MatchesMonePaletteSurfaceDark()
    {
        Assert.Equal(new MudColor(MonePalette.SurfaceDark).Value, MoneTheme.Instance.PaletteDark.Surface.Value);
    }
}
